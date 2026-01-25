using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using Emgu.CV.Dnn;
using Newtonsoft.Json;
using PaddleOCRSharp;

namespace GI_Subtitles
{


    public class OCRTestResult
    {
        public string FileName { get; set; }
        public string OCRText { get; set; }
        public double DurationMs { get; set; }
    }

    public class Summary
    {
        public List<OCRTestResult> Results { get; set; } = new List<OCRTestResult>();
        public double AverageDurationMs { get; set; }
    }

    public class OCRSummary
    {

        public static void ProcessFolder(string testOcrFolderPath, PaddleOCREngine engine)
        {
            if (!Directory.Exists(testOcrFolderPath))
                throw new DirectoryNotFoundException($"Directory not found: {testOcrFolderPath}");

            var pngFiles = Directory.GetFiles(testOcrFolderPath, "*.JPG", SearchOption.TopDirectoryOnly)
                                    .OrderBy(f => f)
                                    .ToList();
            Logger.Log.Debug($"Total files: {pngFiles.Count}");

            var results = new List<OCRTestResult>();
            var totalDuration = 0.0;

            foreach (var file in pngFiles)
            {
                string fileName = Path.GetFileName(file);
                Logger.Log.Debug($"Processing: {fileName}");
                Bitmap bitmap;

                try
                {
                    // Load image
                    bitmap = (Bitmap)Bitmap.FromFile(file);

                    // Perform OCR and time it
                    var sw = Stopwatch.StartNew();
                    OCRResult ocrResult = engine.DetectText(bitmap);
                    sw.Stop();

                    string ocrText = ocrResult?.Text ?? string.Empty;
                    double durationMs = sw.Elapsed.TotalMilliseconds;
                    totalDuration += durationMs;

                    results.Add(new OCRTestResult
                    {
                        FileName = fileName,
                        OCRText = ocrText,
                        DurationMs = durationMs
                    });
                }
                catch (Exception ex)
                {
                    // Log or handle error (e.g., corrupted image)
                    results.Add(new OCRTestResult
                    {
                        FileName = fileName,
                        OCRText = $"[ERROR: {ex.Message}]",
                        DurationMs = -1
                    });
                }
                finally
                {
                    // Ensure bitmap is disposed if it was created
                    // Note: In this version, 'bitmap' is created inside the using block and disposed there
                    // If ImageProcessor.EnhanceTextInImage returns a new bitmap, you may need to dispose it explicitly
                }
            }

            double averageDuration = results.Count > 0
                ? totalDuration / results.Count
                : 0;

            var summary = new Summary
            {
                Results = results,
                AverageDurationMs = Math.Round(averageDuration, 2)
            };

            var contentJson = JsonConvert.SerializeObject(summary, Formatting.Indented);
            File.WriteAllText("result.json", contentJson);
        }
    }



}
