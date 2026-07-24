using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectCounterApp.Core
{
    internal static class Nms
    {
        public static List<Box> Run(List<Box> candidates, float iouThreshold)
        {
            var kept = new List<Box>();
            foreach (var candidate in candidates.OrderByDescending(b => b.Score))
            {
                bool overlapsKept = false;
                foreach (var keptBox in kept)
                {
                    if (ComputeIoU(candidate, keptBox) > iouThreshold)
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

        private static float ComputeIoU(Box a, Box b)
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
