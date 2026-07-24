using ObjectCounterApp.Core;
using ObjectCounterApp.Web.Models;

namespace ObjectCounterApp.Web.Services
{
    public interface IPeopleService
    {
        IReadOnlyList<string> ListNames();
        IReadOnlyList<PersonDetailsDto> GetDetails();
        Task<EnrollResultDto> EnrollAsync(string name, IReadOnlyList<IFormFile> photos);
        bool Remove(string name);
        RenameResult Rename(string oldName, string newName);
    }

    public sealed class PeopleService : IPeopleService
    {
        private readonly IPersonIdentifier _personIdentifier;
        private readonly IEnrolledPeopleStore _peopleStore;
        private readonly ITempFileService _tempFileService;

        public PeopleService(IPersonIdentifier personIdentifier, IEnrolledPeopleStore peopleStore, ITempFileService tempFileService)
        {
            _personIdentifier = personIdentifier;
            _peopleStore = peopleStore;
            _tempFileService = tempFileService;
        }

        public IReadOnlyList<string> ListNames() => _peopleStore.ListNames();

        public IReadOnlyList<PersonDetailsDto> GetDetails()
        {
            return _peopleStore.GetSummaries().Select(s => new PersonDetailsDto(
                s.Name,
                s.PhotoCount,
                s.ThumbnailBase64 is not null ? $"data:{s.ThumbnailContentType};base64,{s.ThumbnailBase64}" : null
            )).ToList();
        }

        public async Task<EnrollResultDto> EnrollAsync(string name, IReadOnlyList<IFormFile> photos)
        {
            int enrolled = 0;
            int failed = 0;

            foreach (var photo in photos)
            {
                await using var tempFile = await _tempFileService.SaveAsync(photo);

                var embedding = _personIdentifier.ComputeEmbeddingForEnrollment(tempFile.Path);
                if (embedding is null)
                {
                    failed++;
                    continue;
                }

                var photoBytes = await File.ReadAllBytesAsync(tempFile.Path);
                var contentType = string.IsNullOrEmpty(photo.ContentType) ? "image/jpeg" : photo.ContentType;
                _peopleStore.AddEmbedding(name, embedding, photoBytes, contentType);
                enrolled++;
            }

            return new EnrollResultDto(name, enrolled, failed);
        }

        public bool Remove(string name) => _peopleStore.Remove(name);

        public RenameResult Rename(string oldName, string newName) => _peopleStore.Rename(oldName, newName);
    }
}
