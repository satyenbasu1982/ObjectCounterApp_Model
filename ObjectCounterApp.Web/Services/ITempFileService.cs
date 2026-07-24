namespace ObjectCounterApp.Web.Services
{
    public interface ITempFileService
    {
        Task<TempFile> SaveAsync(IFormFile file);
    }

    // A scoped handle to a temp file on disk - deleting it on DisposeAsync
    // structurally guarantees cleanup runs even on exception, the same
    // guarantee a try/finally around the file's lifetime gives, but without
    // every caller having to repeat that try/finally themselves.
    public readonly struct TempFile : IAsyncDisposable
    {
        public string Path { get; }

        public TempFile(string path)
        {
            Path = path;
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(Path);
            return ValueTask.CompletedTask;
        }
    }

    public sealed class TempFileService : ITempFileService
    {
        private readonly string _directory;

        public TempFileService()
        {
            _directory = Path.Combine(Path.GetTempPath(), "objectcounterapp");
            Directory.CreateDirectory(_directory);
        }

        public async Task<TempFile> SaveAsync(IFormFile file)
        {
            var path = Path.Combine(_directory, $"{Guid.NewGuid()}.jpg");
            await using (var stream = File.Create(path))
            {
                await file.CopyToAsync(stream);
            }
            return new TempFile(path);
        }
    }
}
