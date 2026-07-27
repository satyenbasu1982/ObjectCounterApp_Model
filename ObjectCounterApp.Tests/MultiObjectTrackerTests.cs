using ObjectCounterApp.Core;

namespace ObjectCounterApp.Tests
{
    public class MultiObjectTrackerTests
    {
        private static readonly DateTime T0 = new(2024, 1, 1, 12, 0, 0);

        private static MultiObjectTracker MakeTracker(double coastingGraceSeconds = 1.5, double attendanceReconfirmSeconds = 20)
        {
            return new MultiObjectTracker(TimeSpan.FromSeconds(coastingGraceSeconds), TimeSpan.FromSeconds(attendanceReconfirmSeconds));
        }

        // Person box centered at (centerX, centerY). When withFace is true, a
        // small face box is nested inside it and IdentityName defaults to
        // "Unknown" (mirroring PersonIdentifier, which never leaves IdentityName
        // null once a face was actually found).
        private static Detection PersonDetection(
            float centerX, float centerY = 0.5f, float halfWidth = 0.08f, float halfHeight = 0.1f,
            string? identityName = null, bool withFace = false)
        {
            float x1 = centerX - halfWidth, x2 = centerX + halfWidth;
            float y1 = centerY - halfHeight, y2 = centerY + halfHeight;

            if (!withFace)
            {
                return new Detection("Person", 0.9f, x1, y1, x2, y2, IsLikelyReal: true, IdentityName: identityName);
            }

            float faceHalf = halfWidth / 4f;
            return new Detection(
                "Person", 0.9f, x1, y1, x2, y2, IsLikelyReal: true, IdentityName: identityName ?? "Unknown",
                FaceX1: centerX - faceHalf, FaceY1: centerY - faceHalf, FaceX2: centerX + faceHalf, FaceY2: centerY + faceHalf);
        }

        [Fact]
        public void Update_AssignsTrackId_ToNewDetection()
        {
            var tracker = MakeTracker();
            var result = tracker.Update("cam", new[] { PersonDetection(0.10f) }, T0);

            Assert.Single(result);
        }

        [Fact]
        public void Update_KeepsSameTrackId_WhenBoxMovesSlightlyAndOverlaps()
        {
            var tracker = MakeTracker();
            var first = tracker.Update("cam", new[] { PersonDetection(0.10f) }, T0);
            var firstId = Assert.Single(first).TrackId;

            var second = tracker.Update("cam", new[] { PersonDetection(0.13f) }, T0.AddSeconds(0.1));
            var track = Assert.Single(second);

            Assert.Equal(firstId, track.TrackId);
        }

        [Fact]
        public void Update_AssignsDistinctTrackIds_ToNonOverlappingDetections()
        {
            var tracker = MakeTracker();
            var result = tracker.Update("cam", new[] { PersonDetection(0.10f), PersonDetection(0.80f) }, T0);

            Assert.Equal(2, result.Count);
            Assert.Equal(2, result.Select(r => r.TrackId).Distinct().Count());
        }

        [Fact]
        public void Update_UsesVelocityPrediction_ToMatchAcrossALargeTimeGap()
        {
            var tracker = MakeTracker();

            var r0 = tracker.Update("cam", new[] { PersonDetection(0.10f) }, T0);
            var trackId = Assert.Single(r0).TrackId;

            tracker.Update("cam", new[] { PersonDetection(0.13f) }, T0.AddSeconds(0.1));
            tracker.Update("cam", new[] { PersonDetection(0.16f) }, T0.AddSeconds(0.2));

            // A 1-second gap (10x the prior frame spacing) at the velocity the
            // track has built up (~0.101 units/sec after those two corrections)
            // puts the true position at 0.24625 - well past where the last raw
            // detection (0.16) was seen, so a naive "match against last raw
            // position" would miss (IoU under the 0.3 threshold). The predicted,
            // velocity-extrapolated box lands exactly on the new detection.
            var r3 = tracker.Update("cam", new[] { PersonDetection(0.24625f) }, T0.AddSeconds(1.2));

            var track = Assert.Single(r3);
            Assert.Equal(trackId, track.TrackId);
        }

        [Fact]
        public void Update_KeepsCoastingTrack_WithinGraceWindow()
        {
            var tracker = MakeTracker(coastingGraceSeconds: 1.5);
            var r0 = tracker.Update("cam", new[] { PersonDetection(0.10f) }, T0);
            var trackId = Assert.Single(r0).TrackId;

            var r1 = tracker.Update("cam", Array.Empty<Detection>(), T0.AddSeconds(1.0));

            var track = Assert.Single(r1);
            Assert.Equal(trackId, track.TrackId);
            Assert.True(track.IsCoasting);
        }

        [Fact]
        public void Update_DropsTrack_AfterCoastingGraceWindowExpires()
        {
            var tracker = MakeTracker(coastingGraceSeconds: 1.5);
            tracker.Update("cam", new[] { PersonDetection(0.10f) }, T0);

            var r1 = tracker.Update("cam", Array.Empty<Detection>(), T0.AddSeconds(2.0));

            Assert.Empty(r1);
        }

        [Fact]
        public void Update_ReacquiresSameTrackId_WhenDetectionReturnsWithinGraceWindow()
        {
            var tracker = MakeTracker(coastingGraceSeconds: 1.5);
            var r0 = tracker.Update("cam", new[] { PersonDetection(0.10f) }, T0);
            var trackId = Assert.Single(r0).TrackId;

            tracker.Update("cam", Array.Empty<Detection>(), T0.AddSeconds(1.0));
            var r2 = tracker.Update("cam", new[] { PersonDetection(0.10f) }, T0.AddSeconds(1.2));

            var track = Assert.Single(r2);
            Assert.Equal(trackId, track.TrackId);
            Assert.False(track.IsCoasting);
        }

        [Fact]
        public void Update_LocksIdentity_OnlyAfterMinVotesToLockAgreeingFaceBearingFrames()
        {
            var tracker = MakeTracker();

            TrackedDetection? last = null;
            for (int i = 0; i < 4; i++)
            {
                var result = tracker.Update("cam", new[] { PersonDetection(0.10f, identityName: "Alice", withFace: true) }, T0.AddSeconds(i * 0.1));
                last = Assert.Single(result);
                if (i < 3)
                {
                    Assert.False(last.IsIdentityLocked);
                }
            }

            Assert.True(last!.IsIdentityLocked);
            Assert.Equal("Alice", last.LockedIdentityName);
        }

        [Fact]
        public void Update_KeepsLock_AfterASingleContradictingVote()
        {
            var tracker = MakeTracker();
            for (int i = 0; i < 4; i++)
            {
                tracker.Update("cam", new[] { PersonDetection(0.10f, identityName: "Alice", withFace: true) }, T0.AddSeconds(i * 0.1));
            }

            var result = tracker.Update("cam", new[] { PersonDetection(0.10f, identityName: "Bob", withFace: true) }, T0.AddSeconds(0.4));

            var track = Assert.Single(result);
            Assert.True(track.IsIdentityLocked);
            Assert.Equal("Alice", track.LockedIdentityName);
        }

        [Fact]
        public void Update_RelocksIdentity_WhenEnoughContradictingVotesBuildANewMajority()
        {
            var tracker = MakeTracker();
            for (int i = 0; i < 4; i++)
            {
                tracker.Update("cam", new[] { PersonDetection(0.10f, identityName: "Alice", withFace: true) }, T0.AddSeconds(i * 0.1));
            }

            TrackedDetection? last = null;
            for (int i = 4; i < 8; i++)
            {
                var result = tracker.Update("cam", new[] { PersonDetection(0.10f, identityName: "Bob", withFace: true) }, T0.AddSeconds(i * 0.1));
                last = Assert.Single(result);
            }

            Assert.True(last!.IsIdentityLocked);
            Assert.Equal("Bob", last.LockedIdentityName);
        }

        [Fact]
        public void Update_DoesNotVote_OnFramesWithNoFaceBox()
        {
            var tracker = MakeTracker();
            for (int i = 0; i < 3; i++)
            {
                tracker.Update("cam", new[] { PersonDetection(0.10f, identityName: "Alice", withFace: true) }, T0.AddSeconds(i * 0.1));
            }

            // Several face-less frames in a row - the person is still there
            // (matched by box), detection just didn't find a face. These must
            // not push votes, so the 3 real votes above never reach the
            // MinVotesToLock=4 floor no matter how many face-less frames follow.
            TrackedDetection? last = null;
            for (int i = 3; i < 10; i++)
            {
                var result = tracker.Update("cam", new[] { PersonDetection(0.10f) }, T0.AddSeconds(i * 0.1));
                last = Assert.Single(result);
            }

            Assert.False(last!.IsIdentityLocked);
        }

        [Fact]
        public void Update_KeepsCameraTrackSets_Independent()
        {
            var tracker = MakeTracker();

            var gate = tracker.Update("gate", new[] { PersonDetection(0.10f) }, T0);
            var workArea = tracker.Update("work-area", new[] { PersonDetection(0.10f) }, T0);

            Assert.Single(gate);
            Assert.Single(workArea);

            var gate2 = tracker.Update("gate", new[] { PersonDetection(0.10f) }, T0.AddSeconds(0.1));
            Assert.Single(gate2);

            // work-area's track is untouched by gate's updates - still just
            // coasting on its own, not duplicated or dropped by the other camera.
            var workArea2 = tracker.Update("work-area", Array.Empty<Detection>(), T0.AddSeconds(0.1));
            Assert.Single(workArea2);
        }

        [Fact]
        public void TryMarkAttendanceTrigger_TrueOnFirstCall_FalseWithinWindow_TrueAfterItElapses()
        {
            var tracker = MakeTracker(attendanceReconfirmSeconds: 20);
            var result = tracker.Update("cam", new[] { PersonDetection(0.10f) }, T0);
            var trackId = Assert.Single(result).TrackId;

            Assert.True(tracker.TryMarkAttendanceTrigger("cam", trackId, T0));
            Assert.False(tracker.TryMarkAttendanceTrigger("cam", trackId, T0.AddSeconds(5)));
            Assert.True(tracker.TryMarkAttendanceTrigger("cam", trackId, T0.AddSeconds(21)));
        }

        [Fact]
        public void TryMarkAttendanceTrigger_ReturnsFalse_ForUnknownTrack()
        {
            var tracker = MakeTracker();
            Assert.False(tracker.TryMarkAttendanceTrigger("cam", 999, T0));
        }

        [Fact]
        public void GetLiveOccupancy_ReturnsZero_ForUnknownCamera()
        {
            var tracker = MakeTracker();
            Assert.Equal(0, tracker.GetLiveOccupancy("nonexistent-camera"));
        }

        [Fact]
        public void GetLiveOccupancy_ReturnsTrackCount_AfterUpdateCreatesTracks()
        {
            var tracker = MakeTracker();
            tracker.Update("cam", new[] { PersonDetection(0.10f), PersonDetection(0.80f) }, T0);

            Assert.Equal(2, tracker.GetLiveOccupancy("cam"));
        }

        [Fact]
        public void GetLiveOccupancy_StillCountsATrack_WhileCoasting()
        {
            var tracker = MakeTracker(coastingGraceSeconds: 1.5);
            tracker.Update("cam", new[] { PersonDetection(0.10f) }, T0);

            tracker.Update("cam", Array.Empty<Detection>(), T0.AddSeconds(1.0));

            Assert.Equal(1, tracker.GetLiveOccupancy("cam"));
        }

        [Fact]
        public void GetLiveOccupancy_DropsToZero_AfterTrackAgesOutPastCoastingGrace()
        {
            var tracker = MakeTracker(coastingGraceSeconds: 1.5);
            tracker.Update("cam", new[] { PersonDetection(0.10f) }, T0);

            tracker.Update("cam", Array.Empty<Detection>(), T0.AddSeconds(2.0));

            Assert.Equal(0, tracker.GetLiveOccupancy("cam"));
        }
    }
}
