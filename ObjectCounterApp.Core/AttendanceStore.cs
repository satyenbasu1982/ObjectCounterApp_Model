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

    public interface IAttendanceStore
    {
        bool RecordSighting(string name, DateTime timestamp);
        IReadOnlyList<EmployeeDaySummary> GetDaySummary(DateOnly date, DateTime? now = null);
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

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return sanitized.Length == 0 ? "_" : sanitized;
        }
    }
}
