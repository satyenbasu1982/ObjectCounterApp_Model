using Moq;
using ObjectCounterApp.Core;
using ObjectCounterApp.Web.Services;

namespace ObjectCounterApp.Tests
{
    public class FootTrafficServiceTests
    {
        [Fact]
        public void GetLiveOccupancy_DelegatesToTracker_WithCorrectCameraId()
        {
            var storeMock = new Mock<IFootTrafficStore>();
            var trackerMock = new Mock<IMultiObjectTracker>();
            trackerMock.Setup(t => t.GetLiveOccupancy("gate")).Returns(4);

            var service = new FootTrafficService(storeMock.Object, trackerMock.Object);
            var result = service.GetLiveOccupancy("gate");

            Assert.Equal(4, result);
            trackerMock.Verify(t => t.GetLiveOccupancy("gate"), Times.Once);
        }

        [Fact]
        public void GetDaySummary_MapsStoreSummary_ToDto()
        {
            var day = new DateOnly(2026, 7, 27);
            var hourly = Enumerable.Repeat(0, 24).ToList();
            hourly[9] = 3;
            var storeSummary = new DayTrafficSummary("gate", 3, hourly);

            var storeMock = new Mock<IFootTrafficStore>();
            storeMock.Setup(s => s.GetDaySummary("gate", day)).Returns(storeSummary);
            var trackerMock = new Mock<IMultiObjectTracker>();

            var service = new FootTrafficService(storeMock.Object, trackerMock.Object);
            var result = service.GetDaySummary("gate", day);

            Assert.Equal("gate", result.CameraId);
            Assert.Equal("2026-07-27", result.Date);
            Assert.Equal(3, result.TotalVisits);
            Assert.Equal(3, result.HourlyCounts[9]);
        }
    }
}
