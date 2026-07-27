using ObjectCounterApp.Core;

namespace ObjectCounterApp.Tests
{
    public class FootTrafficStoreTests
    {
        private static FootTrafficStore MakeStore() =>
            new(Path.Combine(Path.GetTempPath(), $"foot-traffic-tests-{Guid.NewGuid()}"));

        [Fact]
        public void RecordVisit_IncrementsTotal_AndCorrectHourBucket_ForNewTrackId()
        {
            var store = MakeStore();
            var date = new DateOnly(2026, 7, 27);
            var timestamp = new DateTime(2026, 7, 27, 9, 30, 0);

            store.RecordVisit("gate", trackId: 1, timestamp);

            var summary = store.GetDaySummary("gate", date);
            Assert.Equal(1, summary.TotalVisits);
            Assert.Equal(1, summary.HourlyCounts[9]);
            Assert.Equal(0, summary.HourlyCounts[10]);
        }

        [Fact]
        public void RecordVisit_SameTrackIdTwice_DoesNotDoubleCount()
        {
            var store = MakeStore();
            var date = new DateOnly(2026, 7, 27);

            store.RecordVisit("gate", trackId: 1, new DateTime(2026, 7, 27, 9, 0, 0));
            store.RecordVisit("gate", trackId: 1, new DateTime(2026, 7, 27, 9, 5, 0));
            store.RecordVisit("gate", trackId: 1, new DateTime(2026, 7, 27, 11, 0, 0));

            var summary = store.GetDaySummary("gate", date);
            Assert.Equal(1, summary.TotalVisits);
            Assert.Equal(1, summary.HourlyCounts[9]);
            Assert.Equal(0, summary.HourlyCounts[11]);
        }

        [Fact]
        public void RecordVisit_CountsDistinctTrackIds_Separately()
        {
            var store = MakeStore();
            var date = new DateOnly(2026, 7, 27);

            store.RecordVisit("gate", trackId: 1, new DateTime(2026, 7, 27, 9, 0, 0));
            store.RecordVisit("gate", trackId: 2, new DateTime(2026, 7, 27, 9, 15, 0));
            store.RecordVisit("gate", trackId: 3, new DateTime(2026, 7, 27, 14, 0, 0));

            var summary = store.GetDaySummary("gate", date);
            Assert.Equal(3, summary.TotalVisits);
            Assert.Equal(2, summary.HourlyCounts[9]);
            Assert.Equal(1, summary.HourlyCounts[14]);
        }

        [Fact]
        public void RecordVisit_KeepsCamerasIsolated()
        {
            var store = MakeStore();
            var date = new DateOnly(2026, 7, 27);

            store.RecordVisit("gate", trackId: 1, new DateTime(2026, 7, 27, 9, 0, 0));
            store.RecordVisit("work-area", trackId: 1, new DateTime(2026, 7, 27, 9, 0, 0));

            Assert.Equal(1, store.GetDaySummary("gate", date).TotalVisits);
            Assert.Equal(1, store.GetDaySummary("work-area", date).TotalVisits);
        }

        [Fact]
        public void RecordVisit_KeepsDaysIsolated()
        {
            var store = MakeStore();

            store.RecordVisit("gate", trackId: 1, new DateTime(2026, 7, 27, 9, 0, 0));
            store.RecordVisit("gate", trackId: 2, new DateTime(2026, 7, 28, 9, 0, 0));

            Assert.Equal(1, store.GetDaySummary("gate", new DateOnly(2026, 7, 27)).TotalVisits);
            Assert.Equal(1, store.GetDaySummary("gate", new DateOnly(2026, 7, 28)).TotalVisits);
        }

        [Fact]
        public void GetDaySummary_ReturnsZeroedSummary_ForUnknownCameraOrDay()
        {
            var store = MakeStore();

            var summary = store.GetDaySummary("nonexistent-camera", new DateOnly(2026, 7, 27));

            Assert.Equal("nonexistent-camera", summary.CameraId);
            Assert.Equal(0, summary.TotalVisits);
            Assert.Equal(24, summary.HourlyCounts.Count);
            Assert.All(summary.HourlyCounts, count => Assert.Equal(0, count));
        }
    }
}
