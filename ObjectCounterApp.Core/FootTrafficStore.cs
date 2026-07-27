using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ObjectCounterApp.Core
{
    public sealed record DayTrafficSummary(string CameraId, int TotalVisits, IReadOnlyList<int> HourlyCounts);

    public interface IFootTrafficStore
    {
        void RecordVisit(string cameraId, int trackId, DateTime timestamp);
        DayTrafficSummary GetDaySummary(string cameraId, DateOnly date);
    }

    // Persists anonymous foot-traffic counts as one folder per day under a root
    // directory, with one JSON file per camera inside each day's folder - the
    // same "poor man's document store" philosophy as AttendanceStore, just
    // keyed by cameraId instead of employee name. A visit counts once, the
    // first time a given TrackId is seen for that camera that day - TrackIds
    // are already stable/unique per visit (assigned once by MultiObjectTracker
    // and never reused), so no identity-flicker-style dedup logic is needed
    // here the way AttendanceStore needed for RecordSighting.
    public sealed class FootTrafficStore : IFootTrafficStore
    {
        private sealed class CameraDayFile
        {
            public string CameraId { get; set; } = string.Empty;
            public HashSet<int> RecordedTrackIds { get; set; } = new();
            public int[] HourlyCounts { get; set; } = new int[24];
        }

        private readonly string _rootPath;
        private readonly object _lock = new();
        private readonly Dictionary<DateOnly, Dictionary<string, CameraDayFile>> _cache = new();

        public FootTrafficStore(string rootPath)
        {
            _rootPath = rootPath;
            Directory.CreateDirectory(_rootPath);
        }

        public void RecordVisit(string cameraId, int trackId, DateTime timestamp)
        {
            var date = DateOnly.FromDateTime(timestamp);

            lock (_lock)
            {
                var day = GetOrLoadDay(date);
                if (!day.TryGetValue(cameraId, out var entry))
                {
                    entry = new CameraDayFile { CameraId = cameraId };
                    day[cameraId] = entry;
                }

                if (!entry.RecordedTrackIds.Add(trackId))
                {
                    return;
                }

                entry.HourlyCounts[timestamp.Hour]++;
                Save(date, entry);
            }
        }

        public DayTrafficSummary GetDaySummary(string cameraId, DateOnly date)
        {
            lock (_lock)
            {
                var day = GetOrLoadDay(date);
                if (!day.TryGetValue(cameraId, out var entry))
                {
                    return new DayTrafficSummary(cameraId, 0, new int[24]);
                }

                return new DayTrafficSummary(cameraId, entry.RecordedTrackIds.Count, entry.HourlyCounts.ToList());
            }
        }

        private Dictionary<string, CameraDayFile> GetOrLoadDay(DateOnly date)
        {
            if (_cache.TryGetValue(date, out var cached))
            {
                return cached;
            }

            var day = new Dictionary<string, CameraDayFile>();
            var dayFolder = DayFolderPath(date);
            if (Directory.Exists(dayFolder))
            {
                foreach (var filePath in Directory.EnumerateFiles(dayFolder, "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(filePath);
                        var entry = JsonSerializer.Deserialize<CameraDayFile>(json);
                        if (entry is not null)
                        {
                            day[entry.CameraId] = entry;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"WARNING: skipping unreadable foot-traffic file '{filePath}': {ex.Message}");
                    }
                }
            }

            _cache[date] = day;
            return day;
        }

        private string DayFolderPath(DateOnly date) => Path.Combine(_rootPath, date.ToString("yyyy-MM-dd"));

        private void Save(DateOnly date, CameraDayFile entry)
        {
            var dayFolder = DayFolderPath(date);
            Directory.CreateDirectory(dayFolder);
            var filePath = Path.Combine(dayFolder, $"{SanitizeFileName(entry.CameraId)}.json");
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
