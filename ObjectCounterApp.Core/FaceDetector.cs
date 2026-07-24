using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;

namespace ObjectCounterApp.Core
{
    public readonly struct FaceCandidate
    {
        public readonly float X1, Y1, X2, Y2, Score;
        public readonly float[] Landmarks; // 10 floats: (x0,y0) .. (x4,y4) = right eye, left eye, nose tip, right mouth corner, left mouth corner

        public FaceCandidate(float x1, float y1, float x2, float y2, float score, float[] landmarks)
        {
            X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; Score = score; Landmarks = landmarks;
        }
    }

    // Wraps the YuNet ONNX face detector (OpenCV Zoo, MIT license). The model has a
    // fixed 640x640 input and 3 detection heads (strides 8/16/32), each producing
    // cls/obj/bbox/kps tensors already shaped (1, cells, channels) so they can be
    // indexed flat as [cellIndex * channels + channel]. Decode logic below is a
    // direct port of OpenCV's face_detect.cpp postProcess(), verified numerically
    // against the reference C++/Python implementation during development.
    public sealed class FaceDetector
    {
        private const int InputSize = 640;
        private static readonly int[] Strides = { 8, 16, 32 };
        // Lowered from the model's own default of 0.6 - real-world webcam faces
        // (glasses glare, partial occlusion, backlight) often score in the
        // 0.4-0.6 range and were being discarded outright. Trade-off: slightly
        // noisier landmarks on marginal detections, shared by every caller
        // (enrollment, still-image detect, live).
        private const float ScoreThreshold = 0.5f;
        private const float NmsIouThreshold = 0.3f;

        // ONNX Runtime InferenceSession.Run is safe to call concurrently from
        // multiple threads (unlike ML.NET's PredictionEngine used by PersonDetector),
        // so no locking is needed here.
        private readonly InferenceSession _session;

        public FaceDetector(string modelPath)
        {
            _session = new InferenceSession(modelPath);
        }

        public FaceCandidate? DetectLargestFace(SKBitmap image)
        {
            var tensor = ImageTensorBuilder.Build(image, InputSize, InputSize, useBgrOrder: true);
            var inputs = new[] { NamedOnnxValue.CreateFromTensor("input", tensor) };

            using var results = _session.Run(inputs);
            var outputs = results.ToDictionary(r => r.Name, r => r.AsEnumerable<float>().ToArray());

            var candidates = Decode(outputs);
            if (candidates.Count == 0)
            {
                return null;
            }

            var best = candidates[0];
            float scaleX = image.Width / (float)InputSize;
            float scaleY = image.Height / (float)InputSize;

            var landmarks = new float[10];
            for (int i = 0; i < 5; i++)
            {
                landmarks[2 * i] = best.Landmarks[2 * i] * scaleX;
                landmarks[2 * i + 1] = best.Landmarks[2 * i + 1] * scaleY;
            }

            return new FaceCandidate(
                best.X1 * scaleX, best.Y1 * scaleY, best.X2 * scaleX, best.Y2 * scaleY,
                best.Score, landmarks);
        }

        private static List<FaceCandidate> Decode(IReadOnlyDictionary<string, float[]> outputs)
        {
            var candidates = new List<FaceCandidate>();

            foreach (var stride in Strides)
            {
                int cols = InputSize / stride;
                int rows = InputSize / stride;

                var cls = outputs[$"cls_{stride}"];
                var obj = outputs[$"obj_{stride}"];
                var bbox = outputs[$"bbox_{stride}"];
                var kps = outputs[$"kps_{stride}"];

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        int idx = r * cols + c;

                        float clsScore = Math.Clamp(cls[idx], 0f, 1f);
                        float objScore = Math.Clamp(obj[idx], 0f, 1f);
                        float score = MathF.Sqrt(clsScore * objScore);
                        if (score < ScoreThreshold)
                        {
                            continue;
                        }

                        float cx = (c + bbox[idx * 4 + 0]) * stride;
                        float cy = (r + bbox[idx * 4 + 1]) * stride;
                        float w = MathF.Exp(bbox[idx * 4 + 2]) * stride;
                        float h = MathF.Exp(bbox[idx * 4 + 3]) * stride;
                        float x1 = cx - w / 2f;
                        float y1 = cy - h / 2f;

                        var landmarks = new float[10];
                        for (int n = 0; n < 5; n++)
                        {
                            landmarks[2 * n] = (kps[idx * 10 + 2 * n] + c) * stride;
                            landmarks[2 * n + 1] = (kps[idx * 10 + 2 * n + 1] + r) * stride;
                        }

                        candidates.Add(new FaceCandidate(x1, y1, x1 + w, y1 + h, score, landmarks));
                    }
                }
            }

            return GreedyNms(candidates, NmsIouThreshold);
        }

        private static List<FaceCandidate> GreedyNms(List<FaceCandidate> candidates, float iouThreshold)
        {
            var kept = new List<FaceCandidate>();
            foreach (var candidate in candidates.OrderByDescending(f => f.Score))
            {
                bool overlapsKept = false;
                foreach (var keptFace in kept)
                {
                    if (ComputeIoU(candidate, keptFace) > iouThreshold)
                    {
                        overlapsKept = true;
                        break;
                    }
                }

                if (!overlapsKept)
                {
                    kept.Add(candidate);
                }
            }
            return kept;
        }

        private static float ComputeIoU(FaceCandidate a, FaceCandidate b)
        {
            float interX1 = Math.Max(a.X1, b.X1);
            float interY1 = Math.Max(a.Y1, b.Y1);
            float interX2 = Math.Min(a.X2, b.X2);
            float interY2 = Math.Min(a.Y2, b.Y2);

            float interWidth = Math.Max(0f, interX2 - interX1);
            float interHeight = Math.Max(0f, interY2 - interY1);
            float intersection = interWidth * interHeight;

            float areaA = Math.Max(0f, a.X2 - a.X1) * Math.Max(0f, a.Y2 - a.Y1);
            float areaB = Math.Max(0f, b.X2 - b.X1) * Math.Max(0f, b.Y2 - b.Y1);
            float union = areaA + areaB - intersection;

            return union <= 0f ? 0f : intersection / union;
        }
    }
}
