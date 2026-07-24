using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace ObjectCounterApp.Core
{
    // Builds a (1,3,height,width) float32 tensor from a bitmap, stretch-resizing to
    // the target size and writing raw 0-255 pixel values (no scaling/mean subtraction),
    // matching OpenCV's dnn::blobFromImage defaults used by YuNet/SFace's reference
    // implementations.
    internal static class ImageTensorBuilder
    {
        public static DenseTensor<float> Build(SKBitmap source, int width, int height, bool useBgrOrder)
        {
            using var resized = ImageOps.Resize(source, width, height);

            var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = resized.GetPixel(x, y);
                    if (useBgrOrder)
                    {
                        tensor[0, 0, y, x] = pixel.Blue;
                        tensor[0, 1, y, x] = pixel.Green;
                        tensor[0, 2, y, x] = pixel.Red;
                    }
                    else
                    {
                        tensor[0, 0, y, x] = pixel.Red;
                        tensor[0, 1, y, x] = pixel.Green;
                        tensor[0, 2, y, x] = pixel.Blue;
                    }
                }
            }

            return tensor;
        }
    }
}
