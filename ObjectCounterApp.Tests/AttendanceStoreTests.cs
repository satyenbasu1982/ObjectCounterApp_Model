using ObjectCounterApp.Core;

namespace ObjectCounterApp.Tests
{
    public class AttendanceStoreTests
    {
        private static AttendanceStore MakeStore() =>
            new(Path.Combine(Path.GetTempPath(), $"attendance-tests-{Guid.NewGuid()}"), TimeSpan.FromMinutes(1));

        [Fact]
        public void UpdateSession_ChangesTimes_AndResortsSessionsIfReordered()
        {
            var store = MakeStore();
            var date = new DateOnly(2026, 1, 15);
            var morning = new DateTime(2026, 1, 15, 9, 0, 0);
            var morningEnd = new DateTime(2026, 1, 15, 9, 5, 0);
            var afternoon = new DateTime(2026, 1, 15, 14, 0, 0);
            var afternoonEnd = new DateTime(2026, 1, 15, 14, 5, 0);

            store.AddSession(date, "Alice", morning, morningEnd);
            store.AddSession(date, "Alice", afternoon, afternoonEnd);

            // Move the morning session (index 0) to later than the afternoon
            // one - GetDaySummary assumes Sessions is chronologically ordered,
            // so this must resort rather than just overwrite in place.
            var newStart = new DateTime(2026, 1, 15, 16, 0, 0);
            var newEnd = new DateTime(2026, 1, 15, 16, 5, 0);
            var result = store.UpdateSession(date, "Alice", 0, newStart, newEnd);

            Assert.Equal(AttendanceEditResult.Success, result);
            var alice = Assert.Single(store.GetDaySummary(date, now: newEnd.AddMinutes(1)));
            Assert.Equal(2, alice.Sessions.Count);
            Assert.Equal(afternoon, alice.Sessions[0].Start);
            Assert.Equal(newStart, alice.Sessions[1].Start);
        }

        [Fact]
        public void UpdateSession_ReturnsEmployeeNotFound_ForUnknownEmployee()
        {
            var store = MakeStore();
            var date = new DateOnly(2026, 1, 15);

            var result = store.UpdateSession(date, "Ghost", 0, DateTime.Now, DateTime.Now.AddMinutes(5));

            Assert.Equal(AttendanceEditResult.EmployeeNotFound, result);
        }

        [Fact]
        public void UpdateSession_ReturnsSessionIndexOutOfRange_ForBadIndex()
        {
            var store = MakeStore();
            var date = new DateOnly(2026, 1, 15);
            var start = new DateTime(2026, 1, 15, 9, 0, 0);
            var end = new DateTime(2026, 1, 15, 9, 5, 0);
            store.AddSession(date, "Eve", start, end);

            var result = store.UpdateSession(date, "Eve", 5, start, end);

            Assert.Equal(AttendanceEditResult.SessionIndexOutOfRange, result);
        }

        [Fact]
        public void UpdateSession_ReturnsInvalidRange_WhenEndIsNotAfterStart()
        {
            var store = MakeStore();
            var date = new DateOnly(2026, 1, 15);
            var start = new DateTime(2026, 1, 15, 9, 0, 0);
            var end = new DateTime(2026, 1, 15, 9, 5, 0);
            store.AddSession(date, "Eve", start, end);

            var result = store.UpdateSession(date, "Eve", 0, end, start);

            Assert.Equal(AttendanceEditResult.InvalidRange, result);
        }

        [Fact]
        public void AddSession_CreatesNewEmployeeEntry_WhenNoneExistedForTheDay()
        {
            var store = MakeStore();
            var date = new DateOnly(2026, 1, 15);
            var start = new DateTime(2026, 1, 15, 9, 0, 0);
            var end = new DateTime(2026, 1, 15, 9, 5, 0);

            var result = store.AddSession(date, "Grace", start, end);

            Assert.Equal(AttendanceEditResult.Success, result);
            var grace = Assert.Single(store.GetDaySummary(date, now: end.AddMinutes(1)));
            Assert.Equal("Grace", grace.Name);
            Assert.Single(grace.Sessions);
        }

        [Fact]
        public void AddSession_ReturnsInvalidRange_WhenEndIsNotAfterStart()
        {
            var store = MakeStore();
            var date = new DateOnly(2026, 1, 15);
            var start = new DateTime(2026, 1, 15, 9, 0, 0);
            var end = new DateTime(2026, 1, 15, 9, 5, 0);

            var result = store.AddSession(date, "Frank", end, start);

            Assert.Equal(AttendanceEditResult.InvalidRange, result);
        }

        [Fact]
        public void DeleteSession_RemovesJustThatSession_WhenOthersRemain()
        {
            var store = MakeStore();
            var date = new DateOnly(2026, 1, 15);
            var morning = new DateTime(2026, 1, 15, 9, 0, 0);
            var morningEnd = new DateTime(2026, 1, 15, 9, 5, 0);
            var afternoon = new DateTime(2026, 1, 15, 14, 0, 0);
            var afternoonEnd = new DateTime(2026, 1, 15, 14, 5, 0);
            store.AddSession(date, "Carol", morning, morningEnd);
            store.AddSession(date, "Carol", afternoon, afternoonEnd);

            var deleted = store.DeleteSession(date, "Carol", 0);

            Assert.True(deleted);
            var carol = Assert.Single(store.GetDaySummary(date, now: afternoonEnd.AddMinutes(1)));
            var session = Assert.Single(carol.Sessions);
            Assert.Equal(afternoon, session.Start);
        }

        [Fact]
        public void DeleteSession_RemovesEmployeeEntirely_AndDeletesFile_WhenItWasTheOnlySession()
        {
            var root = Path.Combine(Path.GetTempPath(), $"attendance-tests-{Guid.NewGuid()}");
            var store = new AttendanceStore(root, TimeSpan.FromMinutes(1));
            var date = new DateOnly(2026, 1, 20);
            var start = new DateTime(2026, 1, 20, 9, 0, 0);
            var end = new DateTime(2026, 1, 20, 9, 5, 0);
            store.AddSession(date, "Bob", start, end);
            var filePath = Path.Combine(root, "2026-01-20", "Bob.json");
            Assert.True(File.Exists(filePath));

            var deleted = store.DeleteSession(date, "Bob", 0);

            Assert.True(deleted);
            Assert.False(File.Exists(filePath));
            Assert.Empty(store.GetDaySummary(date, now: end.AddMinutes(1)));
        }

        [Fact]
        public void DeleteSession_ReturnsFalse_ForUnknownEmployeeOrBadIndex()
        {
            var store = MakeStore();
            var date = new DateOnly(2026, 1, 15);
            var start = new DateTime(2026, 1, 15, 9, 0, 0);
            var end = new DateTime(2026, 1, 15, 9, 5, 0);
            store.AddSession(date, "Dave", start, end);

            Assert.False(store.DeleteSession(date, "NoSuchPerson", 0));
            Assert.False(store.DeleteSession(date, "Dave", 5));
        }
    }
}
