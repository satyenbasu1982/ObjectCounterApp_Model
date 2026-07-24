namespace ObjectCounterApp.Core
{
    // X1,Y1,X2,Y2 are normalized to [0,1] fractions of the image's width/height.
    // IsLikelyReal is true when the model's "person" class score beat its
    // "person-like" (non-real: mannequin/poster/cutout/etc.) class score.
    // IdentityName is null unless identity matching was requested (see
    // PersonIdentifier) - "Unknown" means matching ran but found no enrolled match.
    public record Detection(string Label, float Score, float X1, float Y1, float X2, float Y2, bool IsLikelyReal, string? IdentityName = null);
}
