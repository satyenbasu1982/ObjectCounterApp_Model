using System;
using System.Collections.Generic;

namespace ObjectCounterApp.Core
{
    public interface IMultiObjectTracker
    {
        // Advances every track for `cameraId` to `timestamp` using this frame's
        // raw detections, and returns the camera's full current live track set
        // (not just this frame's matches) - a coasting/occluded track still
        // needs to be returned with its predicted box so a stateless client can
        // render it without keeping any tracking memory of its own.
        IReadOnlyList<TrackedDetection> Update(string cameraId, IReadOnlyList<Detection> detections, DateTime timestamp);

        // Call only when actually about to write an attendance sighting for this
        // track. Returns true (and starts/resets the reconfirm window) on the
        // track's first identity lock or once the configured reconfirm interval
        // has elapsed since the last trigger; false otherwise (including if the
        // track no longer exists). This is what keeps AttendanceStore from being
        // written every single frame a track stays locked.
        bool TryMarkAttendanceTrigger(string cameraId, int trackId, DateTime now);

        // Count of this camera's currently-held tracks (Active + Coasting),
        // 0 if the camera is unknown. A live read of state the tracker
        // already maintains - accurate only while that camera's Update is
        // actively being called; a camera that's stopped sending frames just
        // holds its last state until the normal coasting timeout would have
        // dropped each track.
        int GetLiveOccupancy(string cameraId);
    }

    // Lightweight per-camera multi-object tracker: constant-velocity (alpha-beta
    // filter, not a full matrix Kalman filter - see class-level design notes)
    // motion prediction + greedy IoU matching + a rolling-window majority-vote
    // identity lock. Deliberately classical/cheap (no learned temporal model) -
    // this problem (stay locked onto the same person across a few flickered or
    // briefly-occluded frames) doesn't need one.
    //
    // State is held entirely in memory, coarse-locked the same way
    // AttendanceStore/EnrolledPeopleStore already do (a single lock around the
    // whole operation, not per-camera locks) - tracking updates are cheap
    // enough that finer-grained locking isn't worth the complexity.
    public sealed class MultiObjectTracker : IMultiObjectTracker
    {
        // How much of the predicted->observed position gap to absorb into
        // position vs. velocity each update. Real-time translations of the
        // client-side tracker's frame-count-based constants (see live.js) -
        // reasonable starting points, expected to need tuning once running
        // against a real camera's actual frame-arrival rate.
        private const float PositionGain = 0.6f;
        private const float VelocityGain = 0.15f;
        private const float MaxVelocityUnitsPerSecond = 2.0f;
        private const double MinDeltaSeconds = 0.02;

        private const float MatchIoUThreshold = 0.3f;
        private const int MinHitsToConfirm = 1;

        private const int IdentityLockWindowSize = 7;
        private const int MinVotesToLock = 4;

        private enum TrackState { Tentative, Active, Coasting }

        private sealed class Track
        {
            public int TrackId;
            public float CenterX, CenterY, HalfWidth, HalfHeight;
            public float VelX, VelY;
            public DateTime LastUpdateTimestamp;
            public DateTime LastSeenTimestamp;
            public TrackState State;
            public int Hits;

            public string Label = string.Empty;
            public float Score;
            public bool IsLikelyReal;

            // Only pushed on frames where a face box was actually found - a
            // frame where detection simply failed isn't evidence the person
            // changed, it's just a gap (same rule the client tracker used).
            public readonly Queue<string> IdentityVotes = new();
            public string? LastIdentityName;
            public string? LockedIdentityName;
            public bool IsIdentityLocked;

            // Retained across coasting frames (not cleared) for display
            // continuity while a track has no fresh detection this frame.
            public float? LastFaceX1, LastFaceY1, LastFaceX2, LastFaceY2;

            public DateTime? LastAttendanceTriggerTimestamp;
        }

        private sealed class CameraState
        {
            public readonly Dictionary<int, Track> Tracks = new();
            public int NextTrackId;
        }

        private readonly object _lock = new();
        private readonly Dictionary<string, CameraState> _cameras = new();
        private readonly TimeSpan _coastingGrace;
        private readonly TimeSpan _attendanceReconfirmInterval;

        public MultiObjectTracker(TimeSpan coastingGrace, TimeSpan attendanceReconfirmInterval)
        {
            _coastingGrace = coastingGrace;
            _attendanceReconfirmInterval = attendanceReconfirmInterval;
        }

        public IReadOnlyList<TrackedDetection> Update(string cameraId, IReadOnlyList<Detection> detections, DateTime timestamp)
        {
            lock (_lock)
            {
                var camera = GetOrCreateCamera(cameraId);
                var tracks = camera.Tracks;

                // 1. Predict every existing track forward to `timestamp`. Box
                // size is held constant, not predicted - only the centroid
                // moves, which is enough to keep matching working across a
                // moving person without a full 4D (position+size) filter.
                var predicted = new Dictionary<int, (float cx, float cy, double dt)>();
                foreach (var track in tracks.Values)
                {
                    double dt = Math.Max((timestamp - track.LastUpdateTimestamp).TotalSeconds, MinDeltaSeconds);
                    predicted[track.TrackId] = (
                        track.CenterX + track.VelX * (float)dt,
                        track.CenterY + track.VelY * (float)dt,
                        dt);
                }

                // 2. Greedy IoU matching between predicted track boxes and this
                // frame's detections. Both sides are person-level boxes, so
                // plain IoU is enough (no face/person containment blend needed
                // like the client tracker used for mixed box sizes).
                var candidates = new List<(int trackId, int detIndex, float iou)>();
                for (int di = 0; di < detections.Count; di++)
                {
                    var det = detections[di];
                    foreach (var track in tracks.Values)
                    {
                        var (pcx, pcy, _) = predicted[track.TrackId];
                        float iou = ComputeIoU(
                            pcx - track.HalfWidth, pcy - track.HalfHeight, pcx + track.HalfWidth, pcy + track.HalfHeight,
                            det.X1, det.Y1, det.X2, det.Y2);
                        if (iou >= MatchIoUThreshold)
                        {
                            candidates.Add((track.TrackId, di, iou));
                        }
                    }
                }
                candidates.Sort((a, b) => b.iou.CompareTo(a.iou));

                var matchedTrackIds = new HashSet<int>();
                var matchedDetIndices = new HashSet<int>();
                var detToTrack = new Dictionary<int, int>();
                foreach (var (trackId, detIndex, _) in candidates)
                {
                    if (matchedTrackIds.Contains(trackId) || matchedDetIndices.Contains(detIndex))
                    {
                        continue;
                    }
                    matchedTrackIds.Add(trackId);
                    matchedDetIndices.Add(detIndex);
                    detToTrack[detIndex] = trackId;
                }

                // 3. Correct matched tracks with this frame's observation.
                for (int di = 0; di < detections.Count; di++)
                {
                    if (!detToTrack.TryGetValue(di, out var trackId))
                    {
                        continue;
                    }

                    var det = detections[di];
                    var track = tracks[trackId];
                    var (predCx, predCy, dt) = predicted[trackId];

                    float detCx = (det.X1 + det.X2) / 2f;
                    float detCy = (det.Y1 + det.Y2) / 2f;
                    float residualX = detCx - predCx;
                    float residualY = detCy - predCy;

                    track.CenterX = predCx + PositionGain * residualX;
                    track.CenterY = predCy + PositionGain * residualY;
                    track.VelX = Math.Clamp(track.VelX + (VelocityGain / (float)dt) * residualX, -MaxVelocityUnitsPerSecond, MaxVelocityUnitsPerSecond);
                    track.VelY = Math.Clamp(track.VelY + (VelocityGain / (float)dt) * residualY, -MaxVelocityUnitsPerSecond, MaxVelocityUnitsPerSecond);
                    track.HalfWidth = (det.X2 - det.X1) / 2f;
                    track.HalfHeight = (det.Y2 - det.Y1) / 2f;

                    track.LastUpdateTimestamp = timestamp;
                    track.LastSeenTimestamp = timestamp;
                    track.Hits++;
                    track.State = track.Hits >= MinHitsToConfirm ? TrackState.Active : TrackState.Tentative;

                    track.Label = det.Label;
                    track.Score = det.Score;
                    track.IsLikelyReal = det.IsLikelyReal;

                    if (det.FaceX1 is not null)
                    {
                        track.LastFaceX1 = det.FaceX1;
                        track.LastFaceY1 = det.FaceY1;
                        track.LastFaceX2 = det.FaceX2;
                        track.LastFaceY2 = det.FaceY2;
                        PushVote(track, det.IdentityName ?? "Unknown");
                        RecomputeLock(track);
                    }
                }

                // 4. Unmatched existing tracks: an unconfirmed track vanishing
                // is just a false-positive blip, drop it outright. A confirmed
                // track starts/continues coasting on its predicted position
                // until the grace window (a real-time analog of the client's
                // MAX_MISSED_FRAMES) elapses since it was last actually seen.
                var toRemove = new List<int>();
                foreach (var track in tracks.Values)
                {
                    if (matchedTrackIds.Contains(track.TrackId))
                    {
                        continue;
                    }

                    if (track.State == TrackState.Tentative)
                    {
                        toRemove.Add(track.TrackId);
                        continue;
                    }

                    var (predCx, predCy, _) = predicted[track.TrackId];
                    track.CenterX = predCx;
                    track.CenterY = predCy;
                    track.LastUpdateTimestamp = timestamp;
                    track.State = TrackState.Coasting;

                    if (timestamp - track.LastSeenTimestamp > _coastingGrace)
                    {
                        toRemove.Add(track.TrackId);
                    }
                }
                foreach (var id in toRemove)
                {
                    tracks.Remove(id);
                }

                // 5. Unmatched detections spawn new tracks with no velocity
                // information yet (assumed stationary until the next frame
                // proves otherwise - a safe default for a person just entering
                // frame).
                for (int di = 0; di < detections.Count; di++)
                {
                    if (matchedDetIndices.Contains(di))
                    {
                        continue;
                    }

                    var det = detections[di];
                    var track = new Track
                    {
                        TrackId = camera.NextTrackId++,
                        CenterX = (det.X1 + det.X2) / 2f,
                        CenterY = (det.Y1 + det.Y2) / 2f,
                        HalfWidth = (det.X2 - det.X1) / 2f,
                        HalfHeight = (det.Y2 - det.Y1) / 2f,
                        VelX = 0f,
                        VelY = 0f,
                        LastUpdateTimestamp = timestamp,
                        LastSeenTimestamp = timestamp,
                        Hits = 1,
                        Label = det.Label,
                        Score = det.Score,
                        IsLikelyReal = det.IsLikelyReal,
                    };
                    track.State = track.Hits >= MinHitsToConfirm ? TrackState.Active : TrackState.Tentative;

                    if (det.FaceX1 is not null)
                    {
                        track.LastFaceX1 = det.FaceX1;
                        track.LastFaceY1 = det.FaceY1;
                        track.LastFaceX2 = det.FaceX2;
                        track.LastFaceY2 = det.FaceY2;
                        PushVote(track, det.IdentityName ?? "Unknown");
                        RecomputeLock(track);
                    }

                    tracks[track.TrackId] = track;
                }

                // 6. Every still-live track (confirmed or coasting) goes out,
                // whether or not it matched this frame - see the interface doc
                // for why.
                var results = new List<TrackedDetection>();
                foreach (var track in tracks.Values)
                {
                    if (track.State == TrackState.Tentative)
                    {
                        continue;
                    }

                    results.Add(new TrackedDetection(
                        track.TrackId, track.Label, track.Score,
                        track.CenterX - track.HalfWidth, track.CenterY - track.HalfHeight,
                        track.CenterX + track.HalfWidth, track.CenterY + track.HalfHeight,
                        track.IsLikelyReal,
                        IsConfirmed: true,
                        IsCoasting: track.State == TrackState.Coasting,
                        IdentityName: track.LastIdentityName,
                        FaceX1: track.LastFaceX1, FaceY1: track.LastFaceY1, FaceX2: track.LastFaceX2, FaceY2: track.LastFaceY2,
                        IsIdentityLocked: track.IsIdentityLocked,
                        LockedIdentityName: track.LockedIdentityName));
                }

                return results;
            }
        }

        public bool TryMarkAttendanceTrigger(string cameraId, int trackId, DateTime now)
        {
            lock (_lock)
            {
                if (!_cameras.TryGetValue(cameraId, out var camera) || !camera.Tracks.TryGetValue(trackId, out var track))
                {
                    return false;
                }

                if (track.LastAttendanceTriggerTimestamp is { } last && now - last < _attendanceReconfirmInterval)
                {
                    return false;
                }

                track.LastAttendanceTriggerTimestamp = now;
                return true;
            }
        }

        public int GetLiveOccupancy(string cameraId)
        {
            lock (_lock)
            {
                if (!_cameras.TryGetValue(cameraId, out var camera))
                {
                    return 0;
                }

                return camera.Tracks.Values.Count(t => t.State != TrackState.Tentative);
            }
        }

        private CameraState GetOrCreateCamera(string cameraId)
        {
            if (!_cameras.TryGetValue(cameraId, out var camera))
            {
                camera = new CameraState();
                _cameras[cameraId] = camera;
            }
            return camera;
        }

        private static void PushVote(Track track, string identityName)
        {
            track.LastIdentityName = identityName;
            track.IdentityVotes.Enqueue(identityName);
            while (track.IdentityVotes.Count > IdentityLockWindowSize)
            {
                track.IdentityVotes.Dequeue();
            }
        }

        // Re-evaluates the lock against the whole current window every time
        // (not "N consecutive matches") - a single contradicting vote only
        // dilutes the window slightly rather than resetting progress, and once
        // a name is locked it only ever changes if a different name earns a
        // strict majority outright (ties favor the currently-locked name).
        // This never actively unlocks back to "no lock" on a weak window - it
        // simply leaves the previous lock state in place, which is the
        // hysteresis the flicker problem needs.
        private static void RecomputeLock(Track track)
        {
            if (track.IdentityVotes.Count < MinVotesToLock)
            {
                return;
            }

            var counts = new Dictionary<string, int>();
            foreach (var vote in track.IdentityVotes)
            {
                counts[vote] = counts.TryGetValue(vote, out var c) ? c + 1 : 1;
            }

            string? bestName = track.LockedIdentityName;
            int bestCount = bestName is not null && counts.TryGetValue(bestName, out var lockedCount) ? lockedCount : 0;

            foreach (var (name, count) in counts)
            {
                if (count > bestCount)
                {
                    bestCount = count;
                    bestName = name;
                }
            }

            if (bestName is not null && bestCount * 2 > track.IdentityVotes.Count)
            {
                track.LockedIdentityName = bestName;
                track.IsIdentityLocked = true;
            }
        }

        private static float ComputeIoU(float ax1, float ay1, float ax2, float ay2, float bx1, float by1, float bx2, float by2)
        {
            float ix1 = Math.Max(ax1, bx1);
            float iy1 = Math.Max(ay1, by1);
            float ix2 = Math.Min(ax2, bx2);
            float iy2 = Math.Min(ay2, by2);
            float interArea = Math.Max(0, ix2 - ix1) * Math.Max(0, iy2 - iy1);
            float areaA = Math.Max(0, ax2 - ax1) * Math.Max(0, ay2 - ay1);
            float areaB = Math.Max(0, bx2 - bx1) * Math.Max(0, by2 - by1);
            float union = areaA + areaB - interArea;
            return union <= 0 ? 0 : interArea / union;
        }
    }
}
