using System;
using SkiaSharp;

namespace ObjectCounterApp.Core
{
    // Two-stage illumination correction applied to the luminance channel before
    // it's embedded (or before face detection - see call sites in
    // PersonIdentifier.cs), so both detection and the embedding are less
    // sensitive to backlight/shadow/uneven exposure than raw pixel values
    // would be:
    //
    //   Stage 1 - Retinex-style illumination-field removal: estimate the
    //   broad, low-frequency lighting gradient (e.g. a bright window behind
    //   the subject) by heavily blurring the luminance channel, then divide
    //   it out and rescale to a fixed target brightness. This is what
    //   actually cancels a backlight gradient - it corrects *global*
    //   under-exposure, which per-tile equalization alone doesn't reach.
    //
    //   Stage 2 - CLAHE (Contrast Limited Adaptive Histogram Equalization):
    //   per-tile local contrast enhancement on top of the Stage-1-corrected
    //   luminance, so fine detail (eyes/nose/mouth edges) still pops. Tiles
    //   are clipped+redistributed first so near-uniform regions like skin
    //   don't get their noise amplified.
    //
    // Chroma (Cb/Cr) is left untouched throughout so this doesn't shift skin
    // tone/color balance.
    //
    // Called from FaceEmbedder.Embed (every embedding call, both enrollment
    // and live-identify) and, before this fix, only there - now also called
    // from PersonIdentifier before face detection, so a face that's too dark
    // for FaceDetector's confidence threshold gets a chance to be found at
    // all, not just recognized once already found.
    internal static class IlluminationNormalizer
    {
        private const int TileGridSize = 8;
        private const int HistogramBins = 256;
        private const double ClipLimitFactor = 3.0; // multiplier of a tile's average per-bin pixel count

        // Stage-1 tuning. The illumination-field blur runs on a downsampled
        // copy (capped at IlluminationDownsampleLongSide) since this method
        // now also runs on person-box crops much larger than the 112x112
        // aligned-face case, and the live-camera loop is unthrottled - every
        // millisecond here is a frame-rate cost. Illumination fields are
        // inherently low-frequency, so estimating one at a small size and
        // upsampling loses essentially nothing.
        private const int IlluminationDownsampleLongSide = 128;
        private const double IlluminationSigmaFraction = 0.20; // fraction of geometric-mean(width,height)
        private const double MinIlluminationSigma = 6.0;
        private const double MaxIlluminationSigma = 40.0; // working size is capped by the downsample above
        private const double IlluminationEpsilon = 8.0;   // noise floor added before dividing
        private const double TargetMeanY = 128.0;          // neutral mid-gray target brightness

        public static SKBitmap Normalize(SKBitmap source)
        {
            int width = source.Width;
            int height = source.Height;

            var y = new double[width, height];
            var cb = new double[width, height];
            var cr = new double[width, height];

            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    var pixel = source.GetPixel(px, py);
                    double r = pixel.Red, g = pixel.Green, b = pixel.Blue;
                    y[px, py] = 0.299 * r + 0.587 * g + 0.114 * b;
                    cb[px, py] = -0.168736 * r - 0.331264 * g + 0.5 * b + 128.0;
                    cr[px, py] = 0.5 * r - 0.418688 * g - 0.081312 * b + 128.0;
                }
            }

            var illum = EstimateIlluminationField(y, width, height);
            var correctedY = CorrectIllumination(y, illum, width, height);

            int tileWidth = Math.Max(1, width / TileGridSize);
            int tileHeight = Math.Max(1, height / TileGridSize);
            int tilesX = (int)Math.Ceiling(width / (double)tileWidth);
            int tilesY = (int)Math.Ceiling(height / (double)tileHeight);

            var tileLuts = new byte[tilesX, tilesY][];
            for (int ty = 0; ty < tilesY; ty++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    tileLuts[tx, ty] = BuildTileLut(correctedY, width, height, tx * tileWidth, ty * tileHeight, tileWidth, tileHeight);
                }
            }

            var output = new SKBitmap(width, height);
            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    double equalizedY = InterpolateTiles(tileLuts, tilesX, tilesY, tileWidth, tileHeight, px, py, correctedY[px, py]);
                    output.SetPixel(px, py, YCbCrToRgb(equalizedY, cb[px, py], cr[px, py]));
                }
            }

            return output;
        }

        private static float ComputeIlluminationSigma(int width, int height)
        {
            double geoMean = Math.Sqrt((double)width * height);
            double sigma = geoMean * IlluminationSigmaFraction;
            return (float)Math.Clamp(sigma, MinIlluminationSigma, MaxIlluminationSigma);
        }

        // Estimates the broad, low-frequency lighting field by Gaussian-blurring
        // luminance at a large sigma via SkiaSharp's filter graph, run on a
        // downsampled copy for speed (see IlluminationDownsampleLongSide) and
        // upsampled back - the same "draw a bitmap through an SKPaint" pattern
        // ImageOps.Resize/Crop already use, just with an ImageFilter attached.
        private static double[,] EstimateIlluminationField(double[,] y, int width, int height)
        {
            using var grayscaleFull = BuildGrayscaleBitmap(y, width, height);

            int longSide = Math.Max(width, height);
            if (longSide <= IlluminationDownsampleLongSide)
            {
                float sigmaFull = ComputeIlluminationSigma(width, height);
                using var blurredFull = BlurBitmap(grayscaleFull, sigmaFull);
                return ReadGrayValues(blurredFull, width, height);
            }

            double scale = (double)IlluminationDownsampleLongSide / longSide;
            int workWidth = Math.Max(1, (int)Math.Round(width * scale));
            int workHeight = Math.Max(1, (int)Math.Round(height * scale));

            using var small = ImageOps.Resize(grayscaleFull, workWidth, workHeight);
            float sigma = ComputeIlluminationSigma(workWidth, workHeight);
            using var blurredSmall = BlurBitmap(small, sigma);
            using var blurredUpscaled = ImageOps.Resize(blurredSmall, width, height);
            return ReadGrayValues(blurredUpscaled, width, height);
        }

        private static SKBitmap BuildGrayscaleBitmap(double[,] y, int width, int height)
        {
            var bitmap = new SKBitmap(width, height);
            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    byte v = (byte)Math.Clamp(Math.Round(y[px, py]), 0, 255);
                    bitmap.SetPixel(px, py, new SKColor(v, v, v));
                }
            }
            return bitmap;
        }

        private static SKBitmap BlurBitmap(SKBitmap source, float sigma)
        {
            var blurred = new SKBitmap(source.Width, source.Height);
            using var canvas = new SKCanvas(blurred);
            using var blurFilter = SKImageFilter.CreateBlur(sigma, sigma);
            using var paint = new SKPaint { ImageFilter = blurFilter };
            canvas.DrawBitmap(source, 0, 0, paint);
            return blurred;
        }

        private static double[,] ReadGrayValues(SKBitmap bitmap, int width, int height)
        {
            var values = new double[width, height];
            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    values[px, py] = bitmap.GetPixel(px, py).Red;
                }
            }
            return values;
        }

        // Retinex-style correction: divides out the broad lighting field and
        // rescales to a fixed target mean, cancelling large-scale gradients
        // (the backlight case) while leaving faster-varying local detail
        // (facial features) for CLAHE to enhance in stage 2. In a truly
        // fully-clipped (zero-signal) region both y and illum are ~0, so the
        // ratio stays ~0 - there's no detail to fabricate, and this correctly
        // leaves it dark rather than amplifying sensor noise.
        private static double[,] CorrectIllumination(double[,] y, double[,] illum, int width, int height)
        {
            var corrected = new double[width, height];
            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    double ratio = y[px, py] / (illum[px, py] + IlluminationEpsilon);
                    corrected[px, py] = Math.Clamp(ratio * TargetMeanY, 0, 255);
                }
            }
            return corrected;
        }

        private static byte[] BuildTileLut(double[,] y, int width, int height, int startX, int startY, int tileWidth, int tileHeight)
        {
            var histogram = new int[HistogramBins];
            int count = 0;
            int endX = Math.Min(startX + tileWidth, width);
            int endY = Math.Min(startY + tileHeight, height);

            for (int py = startY; py < endY; py++)
            {
                for (int px = startX; px < endX; px++)
                {
                    int bin = Math.Clamp((int)Math.Round(y[px, py]), 0, HistogramBins - 1);
                    histogram[bin]++;
                    count++;
                }
            }

            if (count == 0)
            {
                var identity = new byte[HistogramBins];
                for (int i = 0; i < HistogramBins; i++) identity[i] = (byte)i;
                return identity;
            }

            int clipLimit = Math.Max(1, (int)(ClipLimitFactor * count / HistogramBins));
            int excess = 0;
            for (int i = 0; i < HistogramBins; i++)
            {
                if (histogram[i] > clipLimit)
                {
                    excess += histogram[i] - clipLimit;
                    histogram[i] = clipLimit;
                }
            }
            int redistribute = excess / HistogramBins;
            int remainder = excess - redistribute * HistogramBins;
            for (int i = 0; i < HistogramBins; i++)
            {
                histogram[i] += redistribute;
                if (i < remainder) histogram[i]++;
            }

            var lut = new byte[HistogramBins];
            double cumulative = 0;
            for (int i = 0; i < HistogramBins; i++)
            {
                cumulative += histogram[i];
                lut[i] = (byte)Math.Clamp(Math.Round(cumulative / count * 255.0), 0, 255);
            }
            return lut;
        }

        // Bilinearly interpolates between the 4 nearest tile LUTs so tile edges
        // don't produce visible seams - the "adaptive" part of CLAHE.
        private static double InterpolateTiles(byte[,][] tileLuts, int tilesX, int tilesY, int tileWidth, int tileHeight, int px, int py, double value)
        {
            double tileX = (px + 0.5) / tileWidth - 0.5;
            double tileY = (py + 0.5) / tileHeight - 0.5;

            int tx0 = Math.Clamp((int)Math.Floor(tileX), 0, tilesX - 1);
            int ty0 = Math.Clamp((int)Math.Floor(tileY), 0, tilesY - 1);
            int tx1 = Math.Clamp(tx0 + 1, 0, tilesX - 1);
            int ty1 = Math.Clamp(ty0 + 1, 0, tilesY - 1);

            double fx = Math.Clamp(tileX - tx0, 0, 1);
            double fy = Math.Clamp(tileY - ty0, 0, 1);

            int bin = Math.Clamp((int)Math.Round(value), 0, HistogramBins - 1);

            double v00 = tileLuts[tx0, ty0][bin];
            double v10 = tileLuts[tx1, ty0][bin];
            double v01 = tileLuts[tx0, ty1][bin];
            double v11 = tileLuts[tx1, ty1][bin];

            double top = v00 * (1 - fx) + v10 * fx;
            double bottom = v01 * (1 - fx) + v11 * fx;
            return top * (1 - fy) + bottom * fy;
        }

        private static SKColor YCbCrToRgb(double y, double cb, double cr)
        {
            double r = y + 1.402 * (cr - 128.0);
            double g = y - 0.344136 * (cb - 128.0) - 0.714136 * (cr - 128.0);
            double b = y + 1.772 * (cb - 128.0);

            return new SKColor(
                (byte)Math.Clamp(Math.Round(r), 0, 255),
                (byte)Math.Clamp(Math.Round(g), 0, 255),
                (byte)Math.Clamp(Math.Round(b), 0, 255));
        }
    }
}
