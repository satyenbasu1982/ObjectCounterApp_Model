namespace ObjectCounterApp.Web.Models
{
    public sealed record AttendanceSessionDto(DateTime Start, DateTime End);

    public sealed record AttendanceEmployeeDto(
        string Name, DateTime FirstIn, DateTime LastSeenOrOut, double TotalMinutes, bool IsPresent,
        IReadOnlyList<AttendanceSessionDto> Sessions);

    public sealed record AttendanceDayResponseDto(string Date, IReadOnlyList<AttendanceEmployeeDto> Employees);
}
