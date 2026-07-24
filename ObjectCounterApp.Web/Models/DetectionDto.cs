namespace ObjectCounterApp.Web.Models
{
    public sealed record DetectionDto(
        string Label, float Score, float X1, float Y1, float X2, float Y2, bool IsLikelyReal,
        string? IdentityName, float? FaceX1, float? FaceY1, float? FaceX2, float? FaceY2);

    public sealed record DetectResponseDto(int Count, IReadOnlyList<DetectionDto> Detections);
}
