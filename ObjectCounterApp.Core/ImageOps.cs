using SkiaSharp;

namespace ObjectCounterApp.Core
{
    // Small SkiaSharp helpers shared by the face pipeline: stretch-resize (matches
    // the same "uniform per-axis stretch, no letterbox" behavior already used by
    // PersonDetector's ML.NET pipeline) and rectangular crop.
    internal static class ImageOps
    {
        private static readonly SKPaint Paint = new() { FilterQuality = SKFilterQuality.Medium };

        public static SKBitmap Resize(SKBitmap source, int width, int height)
        {
            var info = new SKImageInfo(width, height, source.ColorType, source.AlphaType);
            var resized = new SKBitmap(info);
            using var canvas = new SKCanvas(resized);
            var destRect = new SKRect(0, 0, width, height);
            canvas.DrawBitmap(source, destRect, Paint);
            return resized;
        }

        public static SKBitmap Crop(SKBitmap source, int x, int y, int width, int height)
        {
            var info = new SKImageInfo(width, height, source.ColorType, source.AlphaType);
            var cropped = new SKBitmap(info);
            using var canvas = new SKCanvas(cropped);
            var sourceRect = new SKRect(x, y, x + width, y + height);
            var destRect = new SKRect(0, 0, width, height);
            canvas.DrawBitmap(source, sourceRect, destRect, Paint);
            return cropped;
        }
    }
}
