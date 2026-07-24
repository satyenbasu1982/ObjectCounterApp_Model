using System;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;

namespace ObjectCounterApp.Core
{
    // Wraps the SFace ONNX face embedding model (OpenCV Zoo, Apache 2.0 license).
    // Expects a 112x112 aligned face crop (see FaceAligner) in RGB channel order
    // (OpenCV's reference feature() call uses swapRB=true against its native BGR
    // Mats, i.e. the network expects RGB), raw 0-255 pixel values. Produces a
    // 128-D embedding, L2-normalized here so IdentityMatcher's cosine similarity
    // reduces to a plain dot product. The crop is illumination-normalized first
    // (see IlluminationNormalizer) so lighting doesn't sway the match as much.
    public sealed class FaceEmbedder
    {
        private const int InputSize = 112;

        private readonly InferenceSession _session;

        public FaceEmbedder(string modelPath)
        {
            _session = new InferenceSession(modelPath);
        }

        public float[] Embed(SKBitmap alignedFace)
        {
            using var normalized = IlluminationNormalizer.Normalize(alignedFace);
            var tensor = ImageTensorBuilder.Build(normalized, InputSize, InputSize, useBgrOrder: false);
            var inputs = new[] { NamedOnnxValue.CreateFromTensor("data", tensor) };

            using var results = _session.Run(inputs);
            var embedding = results.First(r => r.Name == "fc1").AsEnumerable<float>().ToArray();

            return L2Normalize(embedding);
        }

        private static float[] L2Normalize(float[] vector)
        {
            double sumSquares = 0;
            foreach (var value in vector)
            {
                sumSquares += value * value;
            }

            double norm = Math.Sqrt(sumSquares);
            if (norm < 1e-12)
            {
                return vector;
            }

            var normalized = new float[vector.Length];
            for (int i = 0; i < vector.Length; i++)
            {
                normalized[i] = (float)(vector[i] / norm);
            }
            return normalized;
        }
    }
}
