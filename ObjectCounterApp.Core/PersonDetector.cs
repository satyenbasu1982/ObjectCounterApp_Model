using System.Collections.Generic;
using Microsoft.ML;

namespace ObjectCounterApp.Core
{
    public interface IPersonDetector
    {
        List<Detection> DetectPersons(string imagePath);
    }

    public sealed class PersonDetector : IPersonDetector
    {
        // Output tensor layout is (1, 4 + nc, totalProposals) = (1, 6, 8400) for this
        // custom 2-class ("person", "person-like") model. Detections are filtered
        // on the "person" class (index 4); the "person-like" class (index 5) score
        // is still read per box so callers can flag likely-non-real detections
        // (mannequins, posters/cutouts, statues, etc.).
        private const int TotalProposals = 8400;
        private const int PersonClassOffset = 4;
        private const int PersonLikeClassOffset = 5;
        private const float ConfidenceThreshold = 0.5f;
        private const float IouThreshold = 0.45f;
        private const float ModelInputSize = 640f;

        private readonly PredictionEngine<ImageInput, PredictionOutput> _predictionEngine;
        private readonly object _lock = new();

        public PersonDetector(string modelPath)
        {
            var mlContext = new MLContext();

            var pipeline = mlContext.Transforms.LoadImages("LoadedImage", imageFolder: "", inputColumnName: nameof(ImageInput.ImagePath))
                .Append(mlContext.Transforms.ResizeImages("ResizedImage", imageWidth: 640, imageHeight: 640, inputColumnName: "LoadedImage"))
                .Append(mlContext.Transforms.ExtractPixels(outputColumnName: "images", inputColumnName: "ResizedImage", scaleImage: 1f / 255f))
                .Append(mlContext.Transforms.ApplyOnnxModel(
                    outputColumnNames: new[] { "output0" },
                    inputColumnNames: new[] { "images" },
                    modelFile: modelPath));

            var emptyData = mlContext.Data.LoadFromEnumerable(new List<ImageInput>());
            var model = pipeline.Fit(emptyData);
            _predictionEngine = mlContext.Model.CreatePredictionEngine<ImageInput, PredictionOutput>(model);
        }

        // PredictionEngine is not thread-safe; this is guarded because the web app
        // may issue overlapping requests (e.g. periodic video-frame detection).
        public List<Detection> DetectPersons(string imagePath)
        {
            PredictionOutput prediction;
            lock (_lock)
            {
                prediction = _predictionEngine.Predict(new ImageInput { ImagePath = imagePath });
            }

            var boxes = FindPersonBoxes(prediction.OutputTensor);

            var detections = new List<Detection>(boxes.Count);
            foreach (var box in boxes)
            {
                detections.Add(new Detection(
                    Label: "Person",
                    Score: box.Score,
                    X1: Clamp01(box.X1 / ModelInputSize),
                    Y1: Clamp01(box.Y1 / ModelInputSize),
                    X2: Clamp01(box.X2 / ModelInputSize),
                    Y2: Clamp01(box.Y2 / ModelInputSize),
                    IsLikelyReal: box.Score > box.PersonLikeScore));
            }

            return detections;
        }

        private static List<Box> FindPersonBoxes(float[] tensor)
        {
            var candidates = new List<Box>();
            for (int i = 0; i < TotalProposals; i++)
            {
                float score = tensor[(PersonClassOffset * TotalProposals) + i];
                if (score <= ConfidenceThreshold)
                {
                    continue;
                }

                float cx = tensor[(0 * TotalProposals) + i];
                float cy = tensor[(1 * TotalProposals) + i];
                float w = tensor[(2 * TotalProposals) + i];
                float h = tensor[(3 * TotalProposals) + i];
                float personLikeScore = tensor[(PersonLikeClassOffset * TotalProposals) + i];
                candidates.Add(new Box(cx, cy, w, h, score, personLikeScore));
            }

            return Nms.Run(candidates, IouThreshold);
        }

        private static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);
    }
}
