namespace ObjectCounterApp.Web.Models
{
    public sealed record FootTrafficDayResponseDto(string CameraId, string Date, int TotalVisits, IReadOnlyList<int> HourlyCounts);
}
