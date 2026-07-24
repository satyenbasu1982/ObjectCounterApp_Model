using Microsoft.AspNetCore.Http;
using Moq;
using ObjectCounterApp.Core;
using ObjectCounterApp.Web.Services;

namespace ObjectCounterApp.Tests
{
    public class PeopleServiceTests
    {
        // ComputeEmbeddingForEnrollment/AddEmbedding both take a real path,
        // and EnrollAsync reads the file's bytes back off disk for
        // successfully-embedded photos - a real (if empty) temp file backs
        // each mocked SaveAsync call, cleaned up by TempFile's own
        // DisposeAsync (File.Delete) at the end of each using scope.
        private static Task<TempFile> CreateRealTempFile(IFormFile _)
        {
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            return Task.FromResult(new TempFile(path));
        }

        private static Mock<IFormFile> MakeFormFileMock(string contentType = "image/jpeg")
        {
            var mock = new Mock<IFormFile>();
            mock.Setup(f => f.ContentType).Returns(contentType);
            return mock;
        }

        [Fact]
        public void ListNames_DelegatesToStore()
        {
            var storeMock = new Mock<IEnrolledPeopleStore>();
            storeMock.Setup(s => s.ListNames()).Returns(new[] { "Satyen", "Saumya" });

            var service = new PeopleService(Mock.Of<IPersonIdentifier>(), storeMock.Object, Mock.Of<ITempFileService>());
            var names = service.ListNames();

            Assert.Equal(new[] { "Satyen", "Saumya" }, names);
        }

        [Fact]
        public void GetDetails_BuildsDataUri_WhenThumbnailPresent()
        {
            var storeMock = new Mock<IEnrolledPeopleStore>();
            storeMock.Setup(s => s.GetSummaries()).Returns(new[]
            {
                ("Satyen", 3, (string?)"BASE64DATA", (string?)"image/png")
            });

            var service = new PeopleService(Mock.Of<IPersonIdentifier>(), storeMock.Object, Mock.Of<ITempFileService>());
            var details = service.GetDetails();

            var person = Assert.Single(details);
            Assert.Equal("Satyen", person.Name);
            Assert.Equal(3, person.PhotoCount);
            Assert.Equal("data:image/png;base64,BASE64DATA", person.Thumbnail);
        }

        [Fact]
        public void GetDetails_ReturnsNullThumbnail_WhenAbsent()
        {
            var storeMock = new Mock<IEnrolledPeopleStore>();
            storeMock.Setup(s => s.GetSummaries()).Returns(new[]
            {
                ("NoPhotoYet", 0, (string?)null, (string?)null)
            });

            var service = new PeopleService(Mock.Of<IPersonIdentifier>(), storeMock.Object, Mock.Of<ITempFileService>());
            var details = service.GetDetails();

            Assert.Null(details[0].Thumbnail);
        }

        [Fact]
        public async Task EnrollAsync_CountsAllPhotosEnrolled_WhenEmbeddingAlwaysFound()
        {
            var identifierMock = new Mock<IPersonIdentifier>();
            identifierMock.Setup(i => i.ComputeEmbeddingForEnrollment(It.IsAny<string>())).Returns(new float[] { 1f, 2f, 3f });

            var storeMock = new Mock<IEnrolledPeopleStore>();

            var tempFileServiceMock = new Mock<ITempFileService>();
            tempFileServiceMock.Setup(t => t.SaveAsync(It.IsAny<IFormFile>())).Returns<IFormFile>(CreateRealTempFile);

            var service = new PeopleService(identifierMock.Object, storeMock.Object, tempFileServiceMock.Object);
            var photos = new[] { MakeFormFileMock().Object, MakeFormFileMock().Object, MakeFormFileMock().Object };

            var result = await service.EnrollAsync("Satyen", photos);

            Assert.Equal("Satyen", result.Name);
            Assert.Equal(3, result.EnrolledPhotos);
            Assert.Equal(0, result.FailedPhotos);
            storeMock.Verify(s => s.AddEmbedding("Satyen", It.IsAny<float[]>(), It.IsAny<byte[]>(), It.IsAny<string>()), Times.Exactly(3));
        }

        [Fact]
        public async Task EnrollAsync_CountsFailedPhotos_WhenEmbeddingNotFound()
        {
            var identifierMock = new Mock<IPersonIdentifier>();
            identifierMock.SetupSequence(i => i.ComputeEmbeddingForEnrollment(It.IsAny<string>()))
                .Returns(new float[] { 1f })
                .Returns((float[]?)null)
                .Returns((float[]?)null);

            var storeMock = new Mock<IEnrolledPeopleStore>();

            var tempFileServiceMock = new Mock<ITempFileService>();
            tempFileServiceMock.Setup(t => t.SaveAsync(It.IsAny<IFormFile>())).Returns<IFormFile>(CreateRealTempFile);

            var service = new PeopleService(identifierMock.Object, storeMock.Object, tempFileServiceMock.Object);
            var photos = new[] { MakeFormFileMock().Object, MakeFormFileMock().Object, MakeFormFileMock().Object };

            var result = await service.EnrollAsync("Satyen", photos);

            Assert.Equal(1, result.EnrolledPhotos);
            Assert.Equal(2, result.FailedPhotos);
            storeMock.Verify(s => s.AddEmbedding("Satyen", It.IsAny<float[]>(), It.IsAny<byte[]>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task EnrollAsync_DefaultsContentType_WhenPhotoContentTypeIsEmpty()
        {
            var identifierMock = new Mock<IPersonIdentifier>();
            identifierMock.Setup(i => i.ComputeEmbeddingForEnrollment(It.IsAny<string>())).Returns(new float[] { 1f });

            string? capturedContentType = null;
            var storeMock = new Mock<IEnrolledPeopleStore>();
            storeMock.Setup(s => s.AddEmbedding(It.IsAny<string>(), It.IsAny<float[]>(), It.IsAny<byte[]>(), It.IsAny<string>()))
                .Callback<string, float[], byte[], string>((_, _, _, contentType) => capturedContentType = contentType);

            var tempFileServiceMock = new Mock<ITempFileService>();
            tempFileServiceMock.Setup(t => t.SaveAsync(It.IsAny<IFormFile>())).Returns<IFormFile>(CreateRealTempFile);

            var service = new PeopleService(identifierMock.Object, storeMock.Object, tempFileServiceMock.Object);
            var photoWithNoContentType = MakeFormFileMock(contentType: "").Object;

            await service.EnrollAsync("Satyen", new[] { photoWithNoContentType });

            Assert.Equal("image/jpeg", capturedContentType);
        }

        [Fact]
        public void Remove_DelegatesToStore()
        {
            var storeMock = new Mock<IEnrolledPeopleStore>();
            storeMock.Setup(s => s.Remove("Satyen")).Returns(true);

            var service = new PeopleService(Mock.Of<IPersonIdentifier>(), storeMock.Object, Mock.Of<ITempFileService>());

            Assert.True(service.Remove("Satyen"));
            storeMock.Verify(s => s.Remove("Satyen"), Times.Once);
        }

        [Theory]
        [InlineData(RenameResult.Success)]
        [InlineData(RenameResult.NotFound)]
        [InlineData(RenameResult.NameTaken)]
        public void Rename_DelegatesToStore_AndReturnsResult(RenameResult expected)
        {
            var storeMock = new Mock<IEnrolledPeopleStore>();
            storeMock.Setup(s => s.Rename("OldName", "NewName")).Returns(expected);

            var service = new PeopleService(Mock.Of<IPersonIdentifier>(), storeMock.Object, Mock.Of<ITempFileService>());
            var result = service.Rename("OldName", "NewName");

            Assert.Equal(expected, result);
        }
    }
}
