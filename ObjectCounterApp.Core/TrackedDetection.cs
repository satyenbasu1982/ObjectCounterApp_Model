namespace ObjectCounterApp.Core
{
    // A Detection enriched with cross-frame tracking state from
    // IMultiObjectTracker. X1,Y1,X2,Y2 are the track's alpha-beta-filtered
    // (already smoothed) person box, normalized to [0,1] like Detection's.
    // FaceX1..Y2 retain Detection's per-frame meaning (or the last-known face
    // box while coasting). IdentityName is the track's current plurality vote
    // across its recent-frame window (see MultiObjectTracker.PushVote), not a
    // single frame's raw result - it's already smoothed before any lock.
    // LockedIdentityName is only meaningful once IsIdentityLocked is true -
    // it reflects the track's strict-majority-voted identity across recent
    // frames, not just this one.
    public record TrackedDetection(
        int TrackId, string Label, float Score, float X1, float Y1, float X2, float Y2,
        bool IsLikelyReal, bool IsConfirmed, bool IsCoasting,
        string? IdentityName, float? FaceX1, float? FaceY1, float? FaceX2, float? FaceY2,
        bool IsIdentityLocked, string? LockedIdentityName);
}
