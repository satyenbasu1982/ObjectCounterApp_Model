using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ObjectCounterApp.Core
{
    public enum RenameResult
    {
        Success,
        NotFound,
        NameTaken
    }

    public interface IEnrolledPeopleStore
    {
        void AddEmbedding(string name, float[] embedding, byte[] photoBytes, string contentType);
        bool Remove(string name);
        IReadOnlyList<string> ListNames();
        IReadOnlyList<(string Name, IReadOnlyList<float[]> Embeddings)> GetAll();
        IReadOnlyList<(string Name, int PhotoCount, string? ThumbnailBase64, string? ThumbnailContentType)> GetSummaries();
        RenameResult Rename(string oldName, string newName);
    }

    // Persists enrolled people (name + one or more reference photos/embeddings) as
    // one JSON file per person under a directory - a "poor man's document store".
    // Independent files mean one corrupt/malformed record can't take down the rest,
    // unlike a single shared JSON file. No real database needed at this scale.
    public sealed class EnrolledPeopleStore : IEnrolledPeopleStore
    {
        private sealed class Photo
        {
            public string ContentType { get; set; } = string.Empty;
            public string PhotoBase64 { get; set; } = string.Empty;
            public float[] Embedding { get; set; } = Array.Empty<float>();
        }

        private sealed class PersonEntry
        {
            public string Name { get; set; } = string.Empty;
            public List<Photo> Photos { get; set; } = new();

            [System.Text.Json.Serialization.JsonIgnore]
            public string FilePath { get; set; } = string.Empty;
        }

        private readonly string _directoryPath;
        private readonly object _lock = new();
        private List<PersonEntry> _people;

        public EnrolledPeopleStore(string directoryPath)
        {
            _directoryPath = directoryPath;
            Directory.CreateDirectory(_directoryPath);
            _people = Load();
        }

        public void AddEmbedding(string name, float[] embedding, byte[] photoBytes, string contentType)
        {
            lock (_lock)
            {
                var entry = _people.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    entry = new PersonEntry
                    {
                        Name = name,
                        FilePath = Path.Combine(_directoryPath, $"{Guid.NewGuid()}.json")
                    };
                    _people.Add(entry);
                }

                entry.Photos.Add(new Photo
                {
                    ContentType = contentType,
                    PhotoBase64 = Convert.ToBase64String(photoBytes),
                    Embedding = embedding
                });

                Save(entry);
            }
        }

        public bool Remove(string name)
        {
            lock (_lock)
            {
                var entry = _people.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    return false;
                }

                _people.Remove(entry);
                File.Delete(entry.FilePath);
                return true;
            }
        }

        public IReadOnlyList<string> ListNames()
        {
            lock (_lock)
            {
                return _people.Select(p => p.Name).ToList();
            }
        }

        public IReadOnlyList<(string Name, IReadOnlyList<float[]> Embeddings)> GetAll()
        {
            lock (_lock)
            {
                return _people
                    .Select(p => (p.Name, (IReadOnlyList<float[]>)p.Photos.Select(photo => photo.Embedding).ToList()))
                    .ToList();
            }
        }

        public IReadOnlyList<(string Name, int PhotoCount, string? ThumbnailBase64, string? ThumbnailContentType)> GetSummaries()
        {
            lock (_lock)
            {
                return _people
                    .Select(p =>
                    {
                        var thumbnail = p.Photos.FirstOrDefault();
                        return (p.Name, p.Photos.Count, thumbnail?.PhotoBase64, thumbnail?.ContentType);
                    })
                    .ToList();
            }
        }

        public RenameResult Rename(string oldName, string newName)
        {
            lock (_lock)
            {
                var entry = _people.FirstOrDefault(p => string.Equals(p.Name, oldName, StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    return RenameResult.NotFound;
                }

                bool collision = _people.Any(p => p != entry && string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase));
                if (collision)
                {
                    return RenameResult.NameTaken;
                }

                entry.Name = newName;
                Save(entry);
                return RenameResult.Success;
            }
        }

        private List<PersonEntry> Load()
        {
            var people = new List<PersonEntry>();
            foreach (var filePath in Directory.EnumerateFiles(_directoryPath, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    var entry = JsonSerializer.Deserialize<PersonEntry>(json);
                    if (entry is not null)
                    {
                        entry.FilePath = filePath;
                        people.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WARNING: skipping unreadable enrollment file '{filePath}': {ex.Message}");
                }
            }
            return people;
        }

        private static void Save(PersonEntry entry)
        {
            var json = JsonSerializer.Serialize(entry);
            File.WriteAllText(entry.FilePath, json);
        }
    }
}
