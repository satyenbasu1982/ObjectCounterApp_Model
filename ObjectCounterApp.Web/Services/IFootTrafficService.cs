using ObjectCounterApp.Core;
using ObjectCounterApp.Web.Models;

namespace ObjectCounterApp.Web.Services
{
    public interface IFootTrafficService
    {
        int GetLiveOccupancy(string cameraId);
        FootTrafficDayResponseDto GetDaySummary(string cameraId, DateOnly date);
    }

    public sealed class FootTrafficService : IFootTrafficService
    {
        private readonly IFootTrafficStore _footTrafficStore;
        private readonly IMultiObjectTracker _tracker;

        public FootTrafficService(IFootTrafficStore footTrafficStore, IMultiObjectTracker tracker)
        {
            _footTrafficStore = footTrafficStore;
            _tracker = tracker;
        }

        public int GetLiveOccupancy(string cameraId) => _tracker.GetLiveOccupancy(cameraId);

        public FootTrafficDayResponseDto GetDaySummary(string cameraId, DateOnly date)
        {
            var summary = _footTrafficStore.GetDaySummary(cameraId, date);
            return new FootTrafficDayResponseDto(summary.CameraId, date.ToString("yyyy-MM-dd"), summary.TotalVisits, summary.HourlyCounts);
        }
    }
}
