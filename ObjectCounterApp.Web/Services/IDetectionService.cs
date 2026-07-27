using ObjectCounterApp.Core;
using ObjectCounterApp.Web.Models;

namespace ObjectCounterApp.Web.Services
{
    public interface IDetectionService
    {
        Task<DetectResponseDto> DetectAsync(IFormFile file, bool identify, bool recordAttendance, string cameraId);
    }

    public sealed class DetectionService : IDetectionService
    {
        // Used by DetectController when the client doesn't send a cameraId -
        // today there's only one live camera view, so every request lands on
        // the same track set. A future second camera (e.g. gate vs. work-area)
        // just needs to start sending a distinct cameraId.
        public const string DefaultCameraId = "default";

        private readonly IPersonDetector _personDetector;
        private readonly IPersonIdentifier _personIdentifier;
        private readonly IAttendanceStore _attendanceStore;
        private readonly ITempFileService _tempFileService;
        private readonly IMultiObjectTracker _tracker;

        public DetectionService(
            IPersonDetector personDetector,
            IPersonIdentifier personIdentifier,
            IAttendanceStore attendanceStore,
            ITempFileService tempFileService,
            IMultiObjectTracker tracker)
        {
            _personDetector = personDetector;
            _personIdentifier = personIdentifier;
            _attendanceStore = attendanceStore;
            _tempFileService = tempFileService;
            _tracker = tracker;
        }

        public async Task<DetectResponseDto> DetectAsync(IFormFile file, bool identify, bool recordAttendance, string cameraId)
        {
            await using var tempFile = await _tempFileService.SaveAsync(file);

            var detections = identify
                ? _personIdentifier.DetectAndIdentify(tempFile.Path)
                : _personDetector.DetectPersons(tempFile.Path);

            var now = DateTime.Now;
            var tracked = _tracker.Update(cameraId, detections, now);

            // Only a track whose identity has actually locked (a majority of
            // recent face-bearing frames agree) can record attendance, and
            // even then only once per reconfirm window - a raw per-frame
            // identity (possibly a one-off flicker) is no longer enough, and
            // a locked track doesn't need a disk write on every single frame
            // it stays locked.
            if (recordAttendance)
            {
                foreach (var t in tracked)
                {
                    if (t.IsIdentityLocked && t.LockedIdentityName is not null && t.LockedIdentityName != "Unknown"
                        && _tracker.TryMarkAttendanceTrigger(cameraId, t.TrackId, now))
                    {
                        _attendanceStore.RecordSighting(t.LockedIdentityName, now);
                    }
                }
            }

            var detectionDtos = tracked.Select(t => new DetectionDto(
                t.Label, t.Score, t.X1, t.Y1, t.X2, t.Y2, t.IsLikelyReal,
                t.IdentityName, t.FaceX1, t.FaceY1, t.FaceX2, t.FaceY2,
                t.TrackId, t.IsConfirmed, t.IsCoasting, t.IsIdentityLocked, t.LockedIdentityName
            )).ToList();

            return new DetectResponseDto(detectionDtos.Count, detectionDtos);
        }
    }
}
