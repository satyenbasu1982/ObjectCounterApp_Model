using Microsoft.AspNetCore.Http;
using Moq;
using ObjectCounterApp.Core;
using ObjectCounterApp.Web.Services;

namespace ObjectCounterApp.Tests
{
    public class DetectionServiceTests
    {
        // Nothing in these tests reads the temp file's actual bytes (both
        // detection paths are mocked out), so a placeholder path is enough -
        // TempFile's DisposeAsync (File.Delete) is a safe no-op on a path
        // that was never created.
        private static Task<TempFile> FakeTempFile(IFormFile _)
        {
            return Task.FromResult(new TempFile(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg")));
        }

        private static (Mock<IPersonDetector>, Mock<IPersonIdentifier>, Mock<IAttendanceStore>, DetectionService) MakeService()
        {
            var personDetectorMock = new Mock<IPersonDetector>();
            var personIdentifierMock = new Mock<IPersonIdentifier>();
            var attendanceStoreMock = new Mock<IAttendanceStore>();

            var tempFileServiceMock = new Mock<ITempFileService>();
            tempFileServiceMock.Setup(t => t.SaveAsync(It.IsAny<IFormFile>())).Returns<IFormFile>(FakeTempFile);

            var service = new DetectionService(
                personDetectorMock.Object, personIdentifierMock.Object, attendanceStoreMock.Object, tempFileServiceMock.Object);

            return (personDetectorMock, personIdentifierMock, attendanceStoreMock, service);
        }

        [Fact]
        public async Task DetectAsync_UsesPersonDetector_NotPersonIdentifier_WhenIdentifyIsFalse()
        {
            var (detectorMock, identifierMock, _, service) = MakeService();
            detectorMock.Setup(d => d.DetectPersons(It.IsAny<string>())).Returns(new List<Detection>());

            await service.DetectAsync(Mock.Of<IFormFile>(), identify: false, recordAttendance: false);

            detectorMock.Verify(d => d.DetectPersons(It.IsAny<string>()), Times.Once);
            identifierMock.Verify(i => i.DetectAndIdentify(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task DetectAsync_UsesPersonIdentifier_NotPersonDetector_WhenIdentifyIsTrue()
        {
            var (detectorMock, identifierMock, _, service) = MakeService();
            identifierMock.Setup(i => i.DetectAndIdentify(It.IsAny<string>())).Returns(new List<Detection>());

            await service.DetectAsync(Mock.Of<IFormFile>(), identify: true, recordAttendance: false);

            identifierMock.Verify(i => i.DetectAndIdentify(It.IsAny<string>()), Times.Once);
            detectorMock.Verify(d => d.DetectPersons(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task DetectAsync_MapsAllDetectionFields_ToDto()
        {
            var (detectorMock, _, _, service) = MakeService();
            var detection = new Detection(
                Label: "Person", Score: 0.83f, X1: 0.1f, Y1: 0.2f, X2: 0.3f, Y2: 0.4f, IsLikelyReal: true,
                IdentityName: "Satyen", FaceX1: 0.15f, FaceY1: 0.25f, FaceX2: 0.28f, FaceY2: 0.35f);
            detectorMock.Setup(d => d.DetectPersons(It.IsAny<string>())).Returns(new List<Detection> { detection });

            var result = await service.DetectAsync(Mock.Of<IFormFile>(), identify: false, recordAttendance: false);

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
        }

        [Fact]
        public async Task DetectAsync_RecordsSighting_OnlyForRealConfirmedIdentities()
        {
            var (_, identifierMock, attendanceStoreMock, service) = MakeService();
            var detections = new List<Detection>
            {
                new("Person", 0.9f, 0, 0, 1, 1, true, IdentityName: "Satyen"),
                new("Person", 0.8f, 0, 0, 1, 1, true, IdentityName: "Unknown"),
                new("Person", 0.7f, 0, 0, 1, 1, true, IdentityName: null),
                new("Person", 0.6f, 0, 0, 1, 1, false, IdentityName: null)
            };
            identifierMock.Setup(i => i.DetectAndIdentify(It.IsAny<string>())).Returns(detections);

            await service.DetectAsync(Mock.Of<IFormFile>(), identify: true, recordAttendance: true);

            attendanceStoreMock.Verify(a => a.RecordSighting("Satyen", It.IsAny<DateTime>()), Times.Once);
            attendanceStoreMock.Verify(a => a.RecordSighting(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task DetectAsync_NeverRecordsSighting_WhenRecordAttendanceIsFalse()
        {
            var (_, identifierMock, attendanceStoreMock, service) = MakeService();
            var detections = new List<Detection> { new("Person", 0.9f, 0, 0, 1, 1, true, IdentityName: "Satyen") };
            identifierMock.Setup(i => i.DetectAndIdentify(It.IsAny<string>())).Returns(detections);

            await service.DetectAsync(Mock.Of<IFormFile>(), identify: true, recordAttendance: false);

            attendanceStoreMock.Verify(a => a.RecordSighting(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }
    }
}
