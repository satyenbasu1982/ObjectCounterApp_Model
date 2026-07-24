namespace ObjectCounterApp.Web.Models
{
    public sealed record PersonDetailsDto(string Name, int PhotoCount, string? Thumbnail);

    public sealed record EnrollResultDto(string Name, int EnrolledPhotos, int FailedPhotos);

    public sealed record RenameRequestDto(string? NewName);
}
