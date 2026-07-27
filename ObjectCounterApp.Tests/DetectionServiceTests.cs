using Microsoft.AspNetCore.Http;
using Moq;
using ObjectCounterApp.Core;
using ObjectCounterApp.Web.Services;

namespace ObjectCounterApp.Tests
{
    public class DetectionServiceTests
    {
        private const string CameraId = "test-camera";

        // Nothing in these tests reads the temp file's actual bytes (both
        // detection paths are mocked out), so a placeholder path is enough -
        // TempFile's DisposeAsync (File.Delete) is a safe no-op on a path
        // that was never created.
        private static Task<TempFile> FakeTempFile(IFormFile _)
        {
            return Task.FromResult(new TempFile(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg")));
        }

        private static (Mock<IPersonDetector>, Mock<IPersonIdentifier>, Mock<IAttendanceStore>, Mock<IMultiObjectTracker>, Mock<IFootTrafficStore>, DetectionService) MakeService()
        {
            var personDetectorMock = new Mock<IPersonDetector>();
            var personIdentifierMock = new Mock<IPersonIdentifier>();
            var attendanceStoreMock = new Mock<IAttendanceStore>();
            var trackerMock = new Mock<IMultiObjectTracker>();
            var footTrafficStoreMock = new Mock<IFootTrafficStore>();

            // Default: echo raw detections through as 1:1 confirmed, unlocked
            // tracks, so tests that don't care about tracking state still get
            // sensible DTOs without each needing its own tracker setup.
            trackerMock
                .Setup(t => t.Update(It.IsAny<string>(), It.IsAny<IReadOnlyList<Detection>>(), It.IsAny<DateTime>()))
                .Returns<string, IReadOnlyList<Detection>, DateTime>((_, detections, _) => detections.Select((d, i) => new TrackedDetection(
                    i, d.Label, d.Score, d.X1, d.Y1, d.X2, d.Y2, d.IsLikelyReal,
                    IsConfirmed: true, IsCoasting: false,
                    IdentityName: d.IdentityName, FaceX1: d.FaceX1, FaceY1: d.FaceY1, FaceX2: d.FaceX2, FaceY2: d.FaceY2,
                    IsIdentityLocked: false, LockedIdentityName: null)).ToList());

            var tempFileServiceMock = new Mock<ITempFileService>();
            tempFileServiceMock.Setup(t => t.SaveAsync(It.IsAny<IFormFile>())).Returns<IFormFile>(FakeTempFile);

            var service = new DetectionService(
                personDetectorMock.Object, personIdentifierMock.Object, attendanceStoreMock.Object,
                tempFileServiceMock.Object, trackerMock.Object, footTrafficStoreMock.Object);

            return (personDetectorMock, personIdentifierMock, attendanceStoreMock, trackerMock, footTrafficStoreMock, service);
        }

        [Fact]
        public async Task DetectAsync_UsesPersonDetector_NotPersonIdentifier_WhenIdentifyIsFalse()
        {
            var (detectorMock, identifierMock, _, _, _, service) = MakeService();
            detectorMock.Setup(d => d.DetectPersons(It.IsAny<string>())).Returns(new List<Detection>());

            await service.DetectAsync(Mock.Of<IFormFile>(), identify: false, recordAttendance: false, cameraId: CameraId);

            detectorMock.Verify(d => d.DetectPersons(It.IsAny<string>()), Times.Once);
            identifierMock.Verify(i => i.DetectAndIdentify(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task DetectAsync_UsesPersonIdentifier_NotPersonDetector_WhenIdentifyIsTrue()
        {
            var (detectorMock, identifierMock, _, _, _, service) = MakeService();
            identifierMock.Setup(i => i.DetectAndIdentify(It.IsAny<string>())).Returns(new List<Detection>());

            await service.DetectAsync(Mock.Of<IFormFile>(), identify: true, recordAttendance: false, cameraId: CameraId);

            identifierMock.Verify(i => i.DetectAndIdentify(It.IsAny<string>()), Times.Once);
            detectorMock.Verify(d => d.DetectPersons(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task DetectAsync_MapsAllTrackedDetectionFields_ToDto()
        {
            var (detectorMock, _, _, trackerMock, _, service) = MakeService();
            detectorMock.Setup(d => d.DetectPersons(It.IsAny<string>())).Returns(new List<Detection>
            {
                new("Person", 0.5f, 0, 0, 1, 1, true)
            });

            var tracked = new TrackedDetection(
                7, "Person", 0.83f, 0.1f, 0.2f, 0.3f, 0.4f, true,
                IsConfirmed: true, IsCoasting: true,
                IdentityName: "Satyen", FaceX1: 0.15f, FaceY1: 0.25f, FaceX2: 0.28f, FaceY2: 0.35f,
                IsIdentityLocked: true, LockedIdentityName: "Satyen");
            trackerMock
                .Setup(t => t.Update(It.IsAny<string>(), It.IsAny<IReadOnlyList<Detection>>(), It.IsAny<DateTime>()))
                .Returns(new List<TrackedDetection> { tracked });

            var result = await service.DetectAsync(Mock.Of<IFormFile>(), identify: false, recordAttendance: false, cameraId: CameraId);

            Assert.Equal(1, result.Count);
            var dto = Assert.Single(result.Detections);
            Assert.Equal("Person", dto.Label);
            Assert.Equal(0.83f, dto.Score);
            Assert.Equal(0.1f, dto.X1);
            Assert.Equal(0.2f, dto.Y1);
            Assert.Equal(0.3f, dto.X2);
            Assert.Equal(0.4f, dto.Y2);
            Assert.True(dto.IsLikelyReal);
            Assert.Equal("Satyen", dto.IdentityName);
            Assert.Equal(0.15f, dto.FaceX1);
            Assert.Equal(0.25f, dto.FaceY1);
            Assert.Equal(0.28f, dto.FaceX2);
            Assert.Equal(0.35f, dto.FaceY2);
            Assert.Equal(7, dto.TrackId);
            Assert.True(dto.IsConfirmed);
            Assert.True(dto.IsCoasting);
            Assert.True(dto.IsIdentityLocked);
            Assert.Equal("Satyen", dto.LockedIdentityName);
        }

        [Fact]
        public async Task DetectAsync_RecordsSighting_WhenIdentityLockedAndTriggerAllowed()
        {
            var (_, identifierMock, attendanceStoreMock, trackerMock, _, service) = MakeService();
            identifierMock.Setup(i => i.DetectAndIdentify(It.IsAny<string>())).Returns(new List<Detection>());

            var tracked = new List<TrackedDetection>
            {
                new(1, "Person", 0.9f, 0, 0, 1, 1, true, IsConfirmed: true, IsCoasting: false,
                    IdentityName: "Satyen", FaceX1: null, FaceY1: null, FaceX2: null, FaceY2: null,
                    IsIdentityLocked: true, LockedIdentityName: "Satyen")
            };
            trackerMock.Setup(t => t.Update(It.IsAny<string>(), It.IsAny<IReadOnlyList<Detection>>(), It.IsAny<DateTime>())).Returns(tracked);
            trackerMock.Setup(t => t.TryMarkAttendanceTrigger(CameraId, 1, It.IsAny<DateTime>())).Returns(true);

            await service.DetectAsync(Mock.Of<IFormFile>(), identify: true, recordAttendance: true, cameraId: CameraId);

            attendanceStoreMock.Verify(a => a.RecordSighting("Satyen", It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task DetectAsync_DoesNotRecordSighting_WhenIdentityNotLocked()
        {
            var (_, identifierMock, attendanceStoreMock, trackerMock, _, service) = MakeService();
            identifierMock.Setup(i => i.DetectAndIdentify(It.IsAny<string>())).Returns(new List<Detection>());

            var tracked = new List<TrackedDetection>
            {
                new(1, "Person", 0.9f, 0, 0, 1, 1, true, IsConfirmed: true, IsCoasting: false,
                    IdentityName: "Satyen", FaceX1: null, FaceY1: null, FaceX2: null, FaceY2: null,
                    IsIdentityLocked: false, LockedIdentityName: null)
            };
            trackerMock.Setup(t => t.Update(It.IsAny<string>(), It.IsAny<IReadOnlyList<Detection>>(), It.IsAny<DateTime>())).Returns(tracked);

            await service.DetectAsync(Mock.Of<IFormFile>(), identify: true, recordAttendance: true, cameraId: CameraId);

            attendanceStoreMock.Verify(a => a.RecordSighting(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task DetectAsync_DoesNotRecordSighting_WhenLockedToUnknown()
        {
            var (_, identifierMock, attendanceStoreMock, trackerMock, _, service) = MakeService();
            identifierMock.Setup(i => i.DetectAndIdentify(It.IsAny<string>())).Returns(new List<Detection>());

            var tracked = new List<TrackedDetection>
            {
                new(1, "Person", 0.9f, 0, 0, 1, 1, true, IsConfirmed: true, IsCoasting: false,
                    IdentityName: "Unknown", FaceX1: null, FaceY1: null, FaceX2: null, FaceY2: null,
                    IsIdentityLocked: true, LockedIdentityName: "Unknown")
            };
            trackerMock.Setup(t => t.Update(It.IsAny<string>(), It.IsAny<IReadOnlyList<Detection>>(), It.IsAny<DateTime>())).Returns(tracked);

            await service.DetectAsync(Mock.Of<IFormFile>(), identify: true, recordAttendance: true, cameraId: CameraId);

            attendanceStoreMock.Verify(a => a.RecordSighting(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task DetectAsync_DoesNotRecordSighting_WhenAttendanceTriggerIsThrottled()
        {
            var (_, identifierMock, attendanceStoreMock, trackerMock, _, service) = MakeService();
            identifierMock.Setup(i => i.DetectAndIdentify(It.IsAny<string>())).Returns(new List<Detection>());

            var tracked = new List<TrackedDetection>
            {
                new(1, "Person", 0.9f, 0, 0, 1, 1, true, IsConfirmed: true, IsCoasting: false,
                    IdentityName: "Satyen", FaceX1: null, FaceY1: null, FaceX2: null, FaceY2: null,
                    IsIdentityLocked: true, LockedIdentityName: "Satyen")
            };
            trackerMock.Setup(t => t.Update(It.IsAny<string>(), It.IsAny<IReadOnlyList<Detection>>(), It.IsAny<DateTime>())).Returns(tracked);
            // Simulates the reconfirm window not having elapsed yet - this is
            // the direct regression test for "no longer writes attendance on
            // every single frame a track stays locked."
            trackerMock.Setup(t => t.TryMarkAttendanceTrigger(CameraId, 1, It.IsAny<DateTime>())).Returns(false);

            await service.DetectAsync(Mock.Of<IFormFile>(), identify: true, recordAttendance: true, cameraId: CameraId);

            attendanceStoreMock.Verify(a => a.RecordSighting(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task DetectAsync_NeverRecordsSighting_WhenRecordAttendanceIsFalse()
        {
            var (_, identifierMock, attendanceStoreMock, trackerMock, _, service) = MakeService();
            identifierMock.Setup(i => i.DetectAndIdentify(It.IsAny<string>())).Returns(new List<Detection>());

            var tracked = new List<TrackedDetection>
            {
                new(1, "Person", 0.9f, 0, 0, 1, 1, true, IsConfirmed: true, IsCoasting: false,
                    IdentityName: "Satyen", FaceX1: null, FaceY1: null, FaceX2: null, FaceY2: null,
                    IsIdentityLocked: true, LockedIdentityName: "Satyen")
            };
            trackerMock.Setup(t => t.Update(It.IsAny<string>(), It.IsAny<IReadOnlyList<Detection>>(), It.IsAny<DateTime>())).Returns(tracked);

            await service.DetectAsync(Mock.Of<IFormFile>(), identify: true, recordAttendance: false, cameraId: CameraId);

            attendanceStoreMock.Verify(a => a.RecordSighting(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
            trackerMock.Verify(t => t.TryMarkAttendanceTrigger(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task DetectAsync_PassesCameraId_ThroughToTracker()
        {
            var (detectorMock, _, _, trackerMock, _, service) = MakeService();
            detectorMock.Setup(d => d.DetectPersons(It.IsAny<string>())).Returns(new List<Detection>());

            await service.DetectAsync(Mock.Of<IFormFile>(), identify: false, recordAttendance: false, cameraId: "gate-camera");

            trackerMock.Verify(t => t.Update("gate-camera", It.IsAny<IReadOnlyList<Detection>>(), It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task DetectAsync_RecordsFootTrafficVisit_ForEachLikelyRealTrackedDetection()
        {
            var (detectorMock, _, _, trackerMock, footTrafficStoreMock, service) = MakeService();
            detectorMock.Setup(d => d.DetectPersons(It.IsAny<string>())).Returns(new List<Detection>());

            var tracked = new List<TrackedDetection>
            {
                new(1, "Person", 0.9f, 0, 0, 1, 1, true, IsConfirmed: true, IsCoasting: false,
                    IdentityName: null, FaceX1: null, FaceY1: null, FaceX2: null, FaceY2: null,
                    IsIdentityLocked: false, LockedIdentityName: null),
                new(2, "Person", 0.8f, 0, 0, 1, 1, true, IsConfirmed: true, IsCoasting: false,
                    IdentityName: null, FaceX1: null, FaceY1: null, FaceX2: null, FaceY2: null,
                    IsIdentityLocked: false, LockedIdentityName: null)
            };
            trackerMock.Setup(t => t.Update(It.IsAny<string>(), It.IsAny<IReadOnlyList<Detection>>(), It.IsAny<DateTime>())).Returns(tracked);

            await service.DetectAsync(Mock.Of<IFormFile>(), identify: false, recordAttendance: false, cameraId: CameraId);

            footTrafficStoreMock.Verify(f => f.RecordVisit(CameraId, 1, It.IsAny<DateTime>()), Times.Once);
            footTrafficStoreMock.Verify(f => f.RecordVisit(CameraId, 2, It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task DetectAsync_DoesNotRecordFootTrafficVisit_ForDetectionsThatArentLikelyReal()
        {
            var (detectorMock, _, _, trackerMock, footTrafficStoreMock, service) = MakeService();
            detectorMock.Setup(d => d.DetectPersons(It.IsAny<string>())).Returns(new List<Detection>());

            var tracked = new List<TrackedDetection>
            {
                new(1, "Person", 0.6f, 0, 0, 1, 1, false, IsConfirmed: true, IsCoasting: false,
                    IdentityName: null, FaceX1: null, FaceY1: null, FaceX2: null, FaceY2: null,
                    IsIdentityLocked: false, LockedIdentityName: null)
            };
            trackerMock.Setup(t => t.Update(It.IsAny<string>(), It.IsAny<IReadOnlyList<Detection>>(), It.IsAny<DateTime>())).Returns(tracked);

            await service.DetectAsync(Mock.Of<IFormFile>(), identify: false, recordAttendance: false, cameraId: CameraId);

            footTrafficStoreMock.Verify(f => f.RecordVisit(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task DetectAsync_RecordsFootTrafficVisit_RegardlessOfIdentifyOrRecordAttendanceFlags()
        {
            var (_, identifierMock, _, trackerMock, footTrafficStoreMock, service) = MakeService();
            identifierMock.Setup(i => i.DetectAndIdentify(It.IsAny<string>())).Returns(new List<Detection>());

            var tracked = new List<TrackedDetection>
            {
                new(1, "Person", 0.9f, 0, 0, 1, 1, true, IsConfirmed: true, IsCoasting: false,
                    IdentityName: null, FaceX1: null, FaceY1: null, FaceX2: null, FaceY2: null,
                    IsIdentityLocked: false, LockedIdentityName: null)
            };
            trackerMock.Setup(t => t.Update(It.IsAny<string>(), It.IsAny<IReadOnlyList<Detection>>(), It.IsAny<DateTime>())).Returns(tracked);

            // identify: true but recordAttendance: false - foot traffic must
            // still be counted, since it isn't gated behind either flag.
            await service.DetectAsync(Mock.Of<IFormFile>(), identify: true, recordAttendance: false, cameraId: CameraId);

            footTrafficStoreMock.Verify(f => f.RecordVisit(CameraId, 1, It.IsAny<DateTime>()), Times.Once);
        }
    }
}
