using ObjectCounterApp.Core;
using ObjectCounterApp.Web.Models;

namespace ObjectCounterApp.Web.Services
{
    public interface IAttendanceService
    {
        AttendanceDayResponseDto GetDaySummary(DateOnly day);
    }

    public sealed class AttendanceService : IAttendanceService
    {
        private readonly IAttendanceStore _attendanceStore;

        public AttendanceService(IAttendanceStore attendanceStore)
        {
            _attendanceStore = attendanceStore;
        }

        public AttendanceDayResponseDto GetDaySummary(DateOnly day)
        {
            var summary = _attendanceStore.GetDaySummary(day);
            var employees = summary.Select(e => new AttendanceEmployeeDto(
                e.Name,
                e.FirstIn,
                e.LastSeenOrOut,
                Math.Round(e.TotalMinutes, 1),
                e.IsPresent,
                e.Sessions.Select(s => new AttendanceSessionDto(s.Start, s.End)).ToList()
            )).ToList();

            return new AttendanceDayResponseDto(day.ToString("yyyy-MM-dd"), employees);
        }
    }
}
