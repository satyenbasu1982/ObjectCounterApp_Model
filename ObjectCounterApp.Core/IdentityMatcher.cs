using System.Collections.Generic;

namespace ObjectCounterApp.Core
{
    public static class IdentityMatcher
    {
        // SFace's own reference implementation default cosine-similarity match
        // threshold, calibrated against LFW-style verification benchmarks. This is
        // a "loose", general-purpose threshold - not tuned for access-control-grade
        // false-accept rates. Revisit if this graduates beyond a demo/prototype.
        public const float DefaultThreshold = 0.363f;

        public static string? Match(
            float[] probeEmbedding,
            IReadOnlyList<(string Name, IReadOnlyList<float[]> Embeddings)> enrolledPeople,
            float threshold = DefaultThreshold)
        {
            string? bestName = null;
            float bestScore = threshold;

            foreach (var person in enrolledPeople)
            {
                foreach (var embedding in person.Embeddings)
                {
                    float similarity = CosineSimilarity(probeEmbedding, embedding);
                    if (similarity > bestScore)
                    {
                        bestScore = similarity;
                        bestName = person.Name;
                    }
                }
            }

            return bestName;
        }

        // Both vectors are already L2-normalized by FaceEmbedder, so cosine
        // similarity reduces to a plain dot product.
        private static float CosineSimilarity(float[] a, float[] b)
        {
            float dot = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
            }
            return dot;
        }
    }
}
