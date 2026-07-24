using System;
using Microsoft.ML.Data;

namespace ObjectCounterApp.Core
{
    public class PredictionOutput
    {
        [VectorType(1, 6, 8400)]
        [ColumnName("output0")]
        public float[] OutputTensor { get; set; } = Array.Empty<float>();
    }
}
