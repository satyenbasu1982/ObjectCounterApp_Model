using ObjectCounterApp.Core;
using ObjectCounterApp.Web.Models;

namespace ObjectCounterApp.Web.Services
{
    public interface IDetectionService
    {
        Task<DetectResponseDto> DetectAsync(IFormFile file, bool identify, bool recordAttendance);
    }

    public sealed class DetectionService : IDetectionService
    {
        private readonly IPersonDetector _personDetector;
        private readonly IPersonIdentifier _personIdentifier;
        private readonly IAttendanceStore _attendanceStore;
        private readonly ITempFileService _tempFileService;

        public DetectionService(
            IPersonDetector personDetector,
            IPersonIdentifier personIdentifier,
            IAttendanceStore attendanceStore,
            ITempFileService tempFileService)
        {
            _personDetector = personDetector;
            _personIdentifier = personIdentifier;
            _attendanceStore = attendanceStore;
            _tempFileService = tempFileService;
        }

        public async Task<DetectResponseDto> DetectAsync(IFormFile file, bool identify, bool recordAttendance)
        {
            await using var tempFile = await _tempFileService.SaveAsync(file);

            var detections = identify
                ? _personIdentifier.DetectAndIdentify(tempFile.Path)
                : _personDetector.DetectPersons(tempFile.Path);

            if (recordAttendance)
            {
                var now = DateTime.Now;
                foreach (var detection in detections)
                {
                    if (detection.IdentityName is not null && detection.IdentityName != "Unknown")
                    {
                        _attendanceStore.RecordSighting(detection.IdentityName, now);
                    }
                }
            }

            var detectionDtos = detections.Select(d => new DetectionDto(
                d.Label, d.Score, d.X1, d.Y1, d.X2, d.Y2, d.IsLikelyReal,
                d.IdentityName, d.FaceX1, d.FaceY1, d.FaceX2, d.FaceY2
            )).ToList();

            return new DetectResponseDto(detectionDtos.Count, detectionDtos);
        }
    }
}
