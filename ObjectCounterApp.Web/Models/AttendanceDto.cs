namespace ObjectCounterApp.Web.Models
{
    public sealed record AttendanceSessionDto(DateTime Start, DateTime End);

    public sealed record AttendanceEmployeeDto(
        string Name, DateTime FirstIn, DateTime LastSeenOrOut, double TotalMinutes, bool IsPresent,
        IReadOnlyList<AttendanceSessionDto> Sessions);

    public sealed record AttendanceDayResponseDto(string Date, IReadOnlyList<AttendanceEmployeeDto> Employees);

    public sealed record SessionEditRequestDto(DateTime Start, DateTime End);

    public sealed record AttendanceRangeEmployeeDto(string Name, double TotalMinutes, int DaysPresent);

    public sealed record AttendanceRangeResponseDto(string StartDate, string EndDate, IReadOnlyList<AttendanceRangeEmployeeDto> Employees);
}
