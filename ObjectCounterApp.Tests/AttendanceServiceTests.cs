using Moq;
using ObjectCounterApp.Core;
using ObjectCounterApp.Web.Services;

namespace ObjectCounterApp.Tests
{
    public class AttendanceServiceTests
    {
        private static EmployeeDaySummary MakeSummary(
            string name, DateTime firstIn, DateTime lastSeenOrOut, double totalMinutes, bool isPresent,
            params AttendanceSession[] sessions)
        {
            return new EmployeeDaySummary(name, firstIn, lastSeenOrOut, totalMinutes, isPresent, sessions);
        }

        [Fact]
        public void GetDaySummary_MapsEmployeesAndSessions_Correctly()
        {
            var day = new DateOnly(2026, 7, 24);
            var session1 = new AttendanceSession(new DateTime(2026, 7, 24, 9, 0, 0), new DateTime(2026, 7, 24, 11, 30, 0));
            var session2 = new AttendanceSession(new DateTime(2026, 7, 24, 12, 15, 0), new DateTime(2026, 7, 24, 13, 0, 0));
            var summary = MakeSummary("Satyen", session1.Start, session2.End, 195.0, true, session1, session2);

            var storeMock = new Mock<IAttendanceStore>();
            storeMock.Setup(s => s.GetDaySummary(day, null)).Returns(new[] { summary });

            var service = new AttendanceService(storeMock.Object);
            var result = service.GetDaySummary(day);

            Assert.Equal("2026-07-24", result.Date);
            Assert.Single(result.Employees);

            var employee = result.Employees[0];
            Assert.Equal("Satyen", employee.Name);
            Assert.Equal(session1.Start, employee.FirstIn);
            Assert.Equal(session2.End, employee.LastSeenOrOut);
            Assert.True(employee.IsPresent);
            Assert.Equal(2, employee.Sessions.Count);
            Assert.Equal(session1.Start, employee.Sessions[0].Start);
            Assert.Equal(session1.End, employee.Sessions[0].End);
            Assert.Equal(session2.Start, employee.Sessions[1].Start);
            Assert.Equal(session2.End, employee.Sessions[1].End);
        }

        [Fact]
        public void GetDaySummary_ReturnsEmptyEmployees_WhenStoreHasNoData()
        {
            var day = new DateOnly(2026, 7, 24);
            var storeMock = new Mock<IAttendanceStore>();
            storeMock.Setup(s => s.GetDaySummary(day, null)).Returns(Array.Empty<EmployeeDaySummary>());

            var service = new AttendanceService(storeMock.Object);
            var result = service.GetDaySummary(day);

            Assert.Equal("2026-07-24", result.Date);
            Assert.Empty(result.Employees);
        }

        [Fact]
        public void GetDaySummary_PassesRequestedDate_ToStore()
        {
            var day = new DateOnly(2025, 12, 31);
            var storeMock = new Mock<IAttendanceStore>();
            storeMock.Setup(s => s.GetDaySummary(day, null)).Returns(Array.Empty<EmployeeDaySummary>());

            var service = new AttendanceService(storeMock.Object);
            service.GetDaySummary(day);

            storeMock.Verify(s => s.GetDaySummary(day, null), Times.Once);
        }

        [Theory]
        [InlineData(150.456, 150.5)]
        [InlineData(150.44, 150.4)]
        [InlineData(0.0, 0.0)]
        [InlineData(59.95, 60.0)]
        public void GetDaySummary_RoundsTotalMinutes_ToOneDecimalPlace(double rawMinutes, double expectedRounded)
        {
            var day = new DateOnly(2026, 7, 24);
            var summary = MakeSummary("Satyen", DateTime.Now, DateTime.Now, rawMinutes, false,
                new AttendanceSession(DateTime.Now, DateTime.Now));

            var storeMock = new Mock<IAttendanceStore>();
            storeMock.Setup(s => s.GetDaySummary(day, null)).Returns(new[] { summary });

            var service = new AttendanceService(storeMock.Object);
            var result = service.GetDaySummary(day);

            Assert.Equal(expectedRounded, result.Employees[0].TotalMinutes);
        }
    }
}
