using System;
using System.Collections.Generic;
using SkiaSharp;

namespace ObjectCounterApp.Core
{
    // Orchestrates person detection + per-person face identity matching:
    // crop each detected person's box -> find their face -> align -> embed ->
    // compare against enrolled people. Only real ("person") detections are
    // checked for identity - "person-like" detections are left as-is.
    public sealed class PersonIdentifier
    {
        private readonly PersonDetector _personDetector;
        private readonly FaceDetector _faceDetector;
        private readonly FaceEmbedder _faceEmbedder;
        private readonly EnrolledPeopleStore _peopleStore;

        public PersonIdentifier(
            PersonDetector personDetector,
            FaceDetector faceDetector,
            FaceEmbedder faceEmbedder,
            EnrolledPeopleStore peopleStore)
        {
            _personDetector = personDetector;
            _faceDetector = faceDetector;
            _faceEmbedder = faceEmbedder;
            _peopleStore = peopleStore;
        }

        public List<Detection> DetectAndIdentify(string imagePath)
        {
            var detections = _personDetector.DetectPersons(imagePath);
            if (detections.Count == 0)
            {
                return detections;
            }

            using var image = SKBitmap.Decode(imagePath);
            var enrolledPeople = _peopleStore.GetAll();

            var results = new List<Detection>(detections.Count);
            foreach (var detection in detections)
            {
                if (!detection.IsLikelyReal)
                {
                    results.Add(detection);
                    continue;
                }

                using var crop = CropToBox(image, detection, out int offsetX, out int offsetY);
                // A downsampled, normalized copy is used only to help detection find
                // the face under backlight - alignment/embedding use the original,
                // full-resolution crop so FaceEmbedder.Embed's own correction isn't
                // applied twice in a row (compounding two Retinex+CLAHE passes
                // over-flattens the image), and quality isn't lost to downsampling.
                using var detectionInput = PrepareForDetection(crop);
                var face = _faceDetector.DetectLargestFace(detectionInput);
                if (face is null)
                {
                    results.Add(detection);
                    continue;
                }

                var landmarks = RescaleLandmarks(face.Value.Landmarks, detectionInput, crop);
                using var aligned = FaceAligner.Align(crop, landmarks);
                var embedding = _faceEmbedder.Embed(aligned);
                var name = IdentityMatcher.Match(embedding, enrolledPeople) ?? "Unknown";

                var faceBox = RescaleFaceBox(face.Value, detectionInput, crop);
                results.Add(detection with
                {
                    IdentityName = name,
                    FaceX1 = (offsetX + faceBox.X1) / image.Width,
                    FaceY1 = (offsetY + faceBox.Y1) / image.Height,
                    FaceX2 = (offsetX + faceBox.X2) / image.Width,
                    FaceY2 = (offsetY + faceBox.Y2) / image.Height
                });
            }

            return results;
        }

        // Used by the enrollment endpoint: a reference photo has no person-box
        // context, so the whole image is searched for a face.
        public float[]? ComputeEmbeddingForEnrollment(string imagePath)
        {
            using var image = SKBitmap.Decode(imagePath);
            using var detectionInput = PrepareForDetection(image);
            var face = _faceDetector.DetectLargestFace(detectionInput);
            if (face is null)
            {
                return null;
            }

            var landmarks = RescaleLandmarks(face.Value.Landmarks, detectionInput, image);
            using var aligned = FaceAligner.Align(image, landmarks);
            return _faceEmbedder.Embed(aligned);
        }

        // Illumination correction and CLAHE are both O(pixels) with per-pixel
        // GetPixel/SetPixel calls (see IlluminationNormalizer) - fine for a
        // 112x112 aligned face, but a full-resolution person-box crop can be
        // hundreds of pixels per side, and the live-camera loop is unthrottled,
        // so that cost lands on every frame. FaceDetector resizes its input to
        // a fixed 640x640 internally regardless (see FaceDetector.cs), so
        // detection loses nothing from working on a smaller image - downsample
        // before correcting, purely to keep this call cheap.
        private const int DetectionAssistLongSide = 256;

        private static SKBitmap PrepareForDetection(SKBitmap crop)
        {
            int longSide = Math.Max(crop.Width, crop.Height);
            if (longSide <= DetectionAssistLongSide)
            {
                return IlluminationNormalizer.Normalize(crop);
            }

            double scale = (double)DetectionAssistLongSide / longSide;
            int smallWidth = Math.Max(1, (int)Math.Round(crop.Width * scale));
            int smallHeight = Math.Max(1, (int)Math.Round(crop.Height * scale));
            using var small = ImageOps.Resize(crop, smallWidth, smallHeight);
            return IlluminationNormalizer.Normalize(small);
        }

        // FaceDetector.DetectLargestFace rescales its output to match whatever
        // bitmap it was given (see FaceDetector.cs), so landmarks found on the
        // (possibly downsampled) detection-assist copy need to be mapped back to
        // the original full-resolution bitmap before alignment.
        private static float[] RescaleLandmarks(float[] landmarks, SKBitmap from, SKBitmap to)
        {
            float scaleX = to.Width / (float)from.Width;
            float scaleY = to.Height / (float)from.Height;
            var rescaled = new float[landmarks.Length];
            for (int i = 0; i < landmarks.Length; i += 2)
            {
                rescaled[i] = landmarks[i] * scaleX;
                rescaled[i + 1] = landmarks[i + 1] * scaleY;
            }
            return rescaled;
        }

        // Same rescale as RescaleLandmarks, applied to the face's own bounding
        // box (discarded everywhere else - PersonIdentifier only used the
        // landmarks - but useful to the client for drawing a box tight to the
        // face instead of the whole person).
        private static (float X1, float Y1, float X2, float Y2) RescaleFaceBox(FaceCandidate face, SKBitmap from, SKBitmap to)
        {
            float scaleX = to.Width / (float)from.Width;
            float scaleY = to.Height / (float)from.Height;
            return (face.X1 * scaleX, face.Y1 * scaleY, face.X2 * scaleX, face.Y2 * scaleY);
        }

        private static SKBitmap CropToBox(SKBitmap image, Detection detection, out int offsetX, out int offsetY)
        {
            int x1 = Math.Clamp((int)(detection.X1 * image.Width), 0, image.Width - 1);
            int y1 = Math.Clamp((int)(detection.Y1 * image.Height), 0, image.Height - 1);
            int x2 = Math.Clamp((int)(detection.X2 * image.Width), x1 + 1, image.Width);
            int y2 = Math.Clamp((int)(detection.Y2 * image.Height), y1 + 1, image.Height);

            offsetX = x1;
            offsetY = y1;
            return ImageOps.Crop(image, x1, y1, x2 - x1, y2 - y1);
        }
    }
}
