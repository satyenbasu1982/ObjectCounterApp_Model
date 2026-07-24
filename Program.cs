using System;
using System.IO;
using ObjectCounterApp.Core;

namespace ObjectCounterApp
{
    class Program
    {
        static void Main(string[] args)
        {
            // These files must be inside your execution folder (bin/Debug/net10.0/)
            string modelPath = "human-detector.onnx";
            string imagePath = args.Length > 0 ? args[0] : "test_image.jpg";

            if (!File.Exists(modelPath) || !File.Exists(imagePath))
            {
                Console.WriteLine("----------------------------------------------------------------");
                Console.WriteLine($"CRITICAL ERROR: '{modelPath}' or '{imagePath}' was not found!");
                Console.WriteLine($"Looking in folder: {AppContext.BaseDirectory}");
                Console.WriteLine("Usage: ObjectCounterApp.exe [imagePath]");
                Console.WriteLine("----------------------------------------------------------------");
                return;
            }

            var detector = new PersonDetector(modelPath);
            int personCount = detector.DetectPersons(imagePath).Count;

            Console.WriteLine("\n=========================================");
            Console.WriteLine($"SUCCESS: Total people counted: {personCount}");
            Console.WriteLine("=========================================\n");
        }
    }
}
