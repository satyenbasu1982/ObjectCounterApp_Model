using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ObjectCounterApp.Core
{
    public sealed record AttendanceSession(DateTime Start, DateTime End);

    public sealed record EmployeeDaySummary(
        string Name,
        DateTime FirstIn,
        DateTime LastSeenOrOut,
        double TotalMinutes,
        bool IsPresent,
        IReadOnlyList<AttendanceSession> Sessions);

    public enum AttendanceEditResult
    {
        Success,
        EmployeeNotFound,
        SessionIndexOutOfRange,
        InvalidRange
    }

    public interface IAttendanceStore
    {
        bool RecordSighting(string name, DateTime timestamp);
        IReadOnlyList<EmployeeDaySummary> GetDaySummary(DateOnly date, DateTime? now = null);
        AttendanceEditResult UpdateSession(DateOnly date, string name, int sessionIndex, DateTime start, DateTime end);
        AttendanceEditResult AddSession(DateOnly date, string name, DateTime start, DateTime end);
        bool DeleteSession(DateOnly date, string name, int sessionIndex);
    }

    // Persists attendance as one folder per day under a root directory, with one
    // JSON file per employee inside each day's folder - the same "poor man's
    // document store" philosophy as EnrolledPeopleStore, adapted so concurrent
    // sightings of different employees never touch the same file. Unlike
    // EnrolledPeopleStore (a small, fixed set loaded fully at startup), attendance
    // data grows one folder per day forever, so days are cached lazily on first
    // touch (a sighting or a query) rather than all loaded up front.
    public sealed class AttendanceStore : IAttendanceStore
    {
        private sealed class EmployeeDayFile
        {
            public string Name { get; set; } = string.Empty;
            public List<SessionEntry> Sessions { get; set; } = new();
        }

        private sealed class SessionEntry
        {
            public DateTime Start { get; set; }
            public DateTime End { get; set; }
        }

        private readonly string _rootPath;
        private readonly TimeSpan _absenceTimeout;
        private readonly object _lock = new();
        private readonly Dictionary<DateOnly, Dictionary<string, EmployeeDayFile>> _cache = new();

        public AttendanceStore(string rootPath, TimeSpan absenceTimeout)
        {
            _rootPath = rootPath;
            _absenceTimeout = absenceTimeout;
            Directory.CreateDirectory(_rootPath);
        }

        // Records one sighting of `name` at `timestamp`. Extends the employee's
        // current session if the gap since they were last seen is under the
        // absence timeout, otherwise starts a brand-new session (a departure and
        // later return). Returns true if this started a new session, false if it
        // extended the existing one.
        public bool RecordSighting(string name, DateTime timestamp)
        {
            var date = DateOnly.FromDateTime(timestamp);

            lock (_lock)
            {
                var day = GetOrLoadDay(date);
                if (!day.TryGetValue(name, out var entry))
                {
                    entry = new EmployeeDayFile { Name = name };
                    day[name] = entry;
                }

                bool isNewSession;
                if (entry.Sessions.Count == 0 || timestamp - entry.Sessions[^1].End >= _absenceTimeout)
                {
                    entry.Sessions.Add(new SessionEntry { Start = timestamp, End = timestamp });
                    isNewSession = true;
                }
                else
                {
                    entry.Sessions[^1].End = timestamp;
                    isNewSession = false;
                }

                Save(date, entry);
                return isNewSession;
            }
        }

        // Manual correction: overwrites one session's Start/End. Re-sorts
        // Sessions afterward - an edit can reorder sessions out of
        // chronological order, and GetDaySummary assumes Sessions[0]/[^1]
        // are the first/last session for the day.
        public AttendanceEditResult UpdateSession(DateOnly date, string name, int sessionIndex, DateTime start, DateTime end)
        {
            if (end <= start)
            {
                return AttendanceEditResult.InvalidRange;
            }

            lock (_lock)
            {
                var day = GetOrLoadDay(date);
                if (!day.TryGetValue(name, out var entry))
                {
                    return AttendanceEditResult.EmployeeNotFound;
                }
                if (sessionIndex < 0 || sessionIndex >= entry.Sessions.Count)
                {
                    return AttendanceEditResult.SessionIndexOutOfRange;
                }

                entry.Sessions[sessionIndex].Start = start;
                entry.Sessions[sessionIndex].End = end;
                entry.Sessions.Sort((a, b) => a.Start.CompareTo(b.Start));

                Save(date, entry);
                return AttendanceEditResult.Success;
            }
        }

        // Manual correction: adds a session an employee never had recorded
        // (e.g. they forgot to walk past the camera at all that day).
        // Creates the employee's day entry if this is their first session.
        public AttendanceEditResult AddSession(DateOnly date, string name, DateTime start, DateTime end)
        {
            if (end <= start)
            {
                return AttendanceEditResult.InvalidRange;
            }

            lock (_lock)
            {
                var day = GetOrLoadDay(date);
                if (!day.TryGetValue(name, out var entry))
                {
                    entry = new EmployeeDayFile { Name = name };
                    day[name] = entry;
                }

                entry.Sessions.Add(new SessionEntry { Start = start, End = end });
                entry.Sessions.Sort((a, b) => a.Start.CompareTo(b.Start));

                Save(date, entry);
                return AttendanceEditResult.Success;
            }
        }

        // Manual correction: removes a wrongly-recorded session. If it was
        // the employee's only session that day, removes them from the day
        // entirely (in memory and on disk) rather than leaving a zero-session
        // entry - GetDaySummary assumes at least one session per employee,
        // and GetOrLoadDay's own load path already skips zero-session files.
        public bool DeleteSession(DateOnly date, string name, int sessionIndex)
        {
            lock (_lock)
            {
                var day = GetOrLoadDay(date);
                if (!day.TryGetValue(name, out var entry))
                {
                    return false;
                }
                if (sessionIndex < 0 || sessionIndex >= entry.Sessions.Count)
                {
                    return false;
                }

                entry.Sessions.RemoveAt(sessionIndex);
                if (entry.Sessions.Count == 0)
                {
                    day.Remove(name);
                    DeleteFile(date, entry.Name);
                }
                else
                {
                    Save(date, entry);
                }

                return true;
            }
        }

        // Pure read - IsPresent is derived against `now` (defaults to DateTime.Now)
        // and never written back to disk, so there's no stale "open session" flag
        // that can get out of sync with reality.
        public IReadOnlyList<EmployeeDaySummary> GetDaySummary(DateOnly date, DateTime? now = null)
        {
            var effectiveNow = now ?? DateTime.Now;

            lock (_lock)
            {
                var day = GetOrLoadDay(date);
                return day.Values
                    .Select(entry =>
                    {
                        var sessions = entry.Sessions.Select(s => new AttendanceSession(s.Start, s.End)).ToList();
                        var totalMinutes = sessions.Sum(s => (s.End - s.Start).TotalMinutes);
                        var isPresent = effectiveNow - sessions[^1].End < _absenceTimeout;
                        return new EmployeeDaySummary(entry.Name, sessions[0].Start, sessions[^1].End, totalMinutes, isPresent, sessions);
                    })
                    .OrderBy(s => s.FirstIn)
                    .ToList();
            }
        }

        private Dictionary<string, EmployeeDayFile> GetOrLoadDay(DateOnly date)
        {
            if (_cache.TryGetValue(date, out var cached))
            {
                return cached;
            }

            var day = new Dictionary<string, EmployeeDayFile>();
            var dayFolder = DayFolderPath(date);
            if (Directory.Exists(dayFolder))
            {
                foreach (var filePath in Directory.EnumerateFiles(dayFolder, "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(filePath);
                        var entry = JsonSerializer.Deserialize<EmployeeDayFile>(json);
                        if (entry is not null && entry.Sessions.Count > 0)
                        {
                            day[entry.Name] = entry;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"WARNING: skipping unreadable attendance file '{filePath}': {ex.Message}");
                    }
                }
            }

            _cache[date] = day;
            return day;
        }

        private string DayFolderPath(DateOnly date) => Path.Combine(_rootPath, date.ToString("yyyy-MM-dd"));

        private void Save(DateOnly date, EmployeeDayFile entry)
        {
            var dayFolder = DayFolderPath(date);
            Directory.CreateDirectory(dayFolder);
            var filePath = Path.Combine(dayFolder, $"{SanitizeFileName(entry.Name)}.json");
            var json = JsonSerializer.Serialize(entry);
            File.WriteAllText(filePath, json);
        }

        private void DeleteFile(DateOnly date, string name)
        {
            var filePath = Path.Combine(DayFolderPath(date), $"{SanitizeFileName(name)}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return sanitized.Length == 0 ? "_" : sanitized;
        }
    }
}
