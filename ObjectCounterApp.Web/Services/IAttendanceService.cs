using System.Text;
using ObjectCounterApp.Core;
using ObjectCounterApp.Web.Models;

namespace ObjectCounterApp.Web.Services
{
    public interface IAttendanceService
    {
        AttendanceDayResponseDto GetDaySummary(DateOnly day);
        AttendanceEditResult UpdateSession(DateOnly date, string name, int sessionIndex, DateTime start, DateTime end);
        AttendanceEditResult AddSession(DateOnly date, string name, DateTime start, DateTime end);
        bool DeleteSession(DateOnly date, string name, int sessionIndex);
        AttendanceRangeResponseDto GetRangeSummary(DateOnly start, DateOnly end);
        byte[] ExportRangeCsv(DateOnly start, DateOnly end);
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

        public AttendanceEditResult UpdateSession(DateOnly date, string name, int sessionIndex, DateTime start, DateTime end) =>
            _attendanceStore.UpdateSession(date, name, sessionIndex, start, end);

        public AttendanceEditResult AddSession(DateOnly date, string name, DateTime start, DateTime end) =>
            _attendanceStore.AddSession(date, name, start, end);

        public bool DeleteSession(DateOnly date, string name, int sessionIndex) =>
            _attendanceStore.DeleteSession(date, name, sessionIndex);

        // Aggregates each day in the range via the existing per-day
        // GetDaySummary - each day is already lazily cached by the store, so
        // a week/month range is just a handful of cheap lookups rather than
        // needing a new range-aware store method.
        public AttendanceRangeResponseDto GetRangeSummary(DateOnly start, DateOnly end)
        {
            var totals = new Dictionary<string, (double Minutes, int Days)>();

            for (var date = start; date <= end; date = date.AddDays(1))
            {
                foreach (var employee in _attendanceStore.GetDaySummary(date))
                {
                    var (minutes, days) = totals.TryGetValue(employee.Name, out var existing) ? existing : (0.0, 0);
                    totals[employee.Name] = (minutes + employee.TotalMinutes, days + 1);
                }
            }

            var employees = totals
                .Select(kv => new AttendanceRangeEmployeeDto(kv.Key, Math.Round(kv.Value.Minutes, 1), kv.Value.Days))
                .OrderBy(e => e.Name)
                .ToList();

            return new AttendanceRangeResponseDto(start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"), employees);
        }

        public byte[] ExportRangeCsv(DateOnly start, DateOnly end)
        {
            var range = GetRangeSummary(start, end);

            var csv = new StringBuilder();
            csv.AppendLine("Name,TotalMinutes,DaysPresent");
            foreach (var employee in range.Employees)
            {
                csv.AppendLine($"{EscapeCsv(employee.Name)},{employee.TotalMinutes},{employee.DaysPresent}");
            }

            return Encoding.UTF8.GetBytes(csv.ToString());
        }

        private static string EscapeCsv(string value) =>
            value.Contains(',') || value.Contains('"')
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;
    }
}
