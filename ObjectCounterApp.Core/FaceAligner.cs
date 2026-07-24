using System;
using SkiaSharp;

namespace ObjectCounterApp.Core
{
    // Aligns a face crop to the standard 112x112 ArcFace/SFace template using the
    // 5 landmark points (right eye, left eye, nose tip, right mouth corner, left
    // mouth corner) from FaceDetector. Fits a similarity transform (uniform scale +
    // rotation + translation) via the classic complex-number least-squares solution,
    // which is mathematically equivalent to OpenCV's SVD-based
    // getSimilarityTransformMatrix for well-conditioned (non-degenerate) landmark
    // configurations - i.e. any real detected face. Verified numerically against
    // OpenCV's FaceRecognizerSF::alignCrop during development (max channel diff of 1,
    // i.e. floating-point/interpolation rounding only).
    public static class FaceAligner
    {
        private const int OutputSize = 112;

        private static readonly (float X, float Y)[] ReferenceTemplate =
        {
            (38.2946f, 51.6963f),
            (73.5318f, 51.5014f),
            (56.0252f, 71.7366f),
            (41.5493f, 92.3655f),
            (70.7299f, 92.2041f)
        };

        // Precomputed mean of ReferenceTemplate (matches OpenCV's hardcoded constant).
        private const float DstMeanX = 56.0262f;
        private const float DstMeanY = 71.9008f;

        public static SKBitmap Align(SKBitmap cropImage, float[] landmarks)
        {
            float srcMeanX = 0f, srcMeanY = 0f;
            for (int i = 0; i < 5; i++)
            {
                srcMeanX += landmarks[2 * i];
                srcMeanY += landmarks[2 * i + 1];
            }
            srcMeanX /= 5f;
            srcMeanY /= 5f;

            double reSum = 0, imSum = 0, denom = 0;
            for (int i = 0; i < 5; i++)
            {
                double sx = landmarks[2 * i] - srcMeanX;
                double sy = landmarks[2 * i + 1] - srcMeanY;
                double dx = ReferenceTemplate[i].X - DstMeanX;
                double dy = ReferenceTemplate[i].Y - DstMeanY;

                reSum += dx * sx + dy * sy;
                imSum += dy * sx - dx * sy;
                denom += sx * sx + sy * sy;
            }

            double scale = Math.Sqrt(reSum * reSum + imSum * imSum) / denom;
            double theta = Math.Atan2(imSum, reSum);
            double cosT = Math.Cos(theta), sinT = Math.Sin(theta);

            // Forward transform mapping src landmark points to the reference template.
            double a = scale * cosT, b = -scale * sinT;
            double c = scale * sinT, d = scale * cosT;
            double tx = DstMeanX - (a * srcMeanX + b * srcMeanY);
            double ty = DstMeanY - (c * srcMeanX + d * srcMeanY);

            // Invert, since we need to sample the source image for each output pixel
            // (matching OpenCV's warpAffine, which inverts a forward-defined matrix
            // by default).
            double det = a * d - b * c;
            double ia = d / det, ib = -b / det;
            double ic = -c / det, id = a / det;
            double itx = -(ia * tx + ib * ty);
            double ity = -(ic * tx + id * ty);

            var output = new SKBitmap(OutputSize, OutputSize);
            for (int y = 0; y < OutputSize; y++)
            {
                for (int x = 0; x < OutputSize; x++)
                {
                    double srcX = ia * x + ib * y + itx;
                    double srcY = ic * x + id * y + ity;
                    output.SetPixel(x, y, SampleBilinear(cropImage, srcX, srcY));
                }
            }

            return output;
        }

        // Bilinear sampling with each out-of-bounds corner treated as black,
        // matching OpenCV's default BORDER_CONSTANT (value 0) behavior in warpAffine.
        private static SKColor SampleBilinear(SKBitmap image, double x, double y)
        {
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            double fx = x - x0;
            double fy = y - y0;

            var p00 = GetPixelOrBlack(image, x0, y0);
            var p10 = GetPixelOrBlack(image, x0 + 1, y0);
            var p01 = GetPixelOrBlack(image, x0, y0 + 1);
            var p11 = GetPixelOrBlack(image, x0 + 1, y0 + 1);

            double r = (p00.Red * (1 - fx) + p10.Red * fx) * (1 - fy) + (p01.Red * (1 - fx) + p11.Red * fx) * fy;
            double g = (p00.Green * (1 - fx) + p10.Green * fx) * (1 - fy) + (p01.Green * (1 - fx) + p11.Green * fx) * fy;
            double b = (p00.Blue * (1 - fx) + p10.Blue * fx) * (1 - fy) + (p01.Blue * (1 - fx) + p11.Blue * fx) * fy;

            return new SKColor(
                (byte)Math.Clamp(Math.Round(r), 0, 255),
                (byte)Math.Clamp(Math.Round(g), 0, 255),
                (byte)Math.Clamp(Math.Round(b), 0, 255));
        }

        private static SKColor GetPixelOrBlack(SKBitmap image, int x, int y)
        {
            if (x < 0 || y < 0 || x >= image.Width || y >= image.Height)
            {
                return new SKColor(0, 0, 0);
            }
            return image.GetPixel(x, y);
        }
    }
}
