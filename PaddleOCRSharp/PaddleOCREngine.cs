using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using OpenCvSharp.Extensions;
using CvPoint = OpenCvSharp.Point;
using CvSize = OpenCvSharp.Size;
using log4net;

namespace PaddleOCRSharp
{
    /// <summary>
    /// Paddle OCR Engine
    /// </summary>
    public class PaddleOCREngine : IDisposable
    {
        private readonly InferenceSession _detSession;
        private readonly InferenceSession _recSession;
        private readonly List<string> _labels;
        private readonly OCRParameter _parameter;
        private readonly DetectionModelSettings _detectionSettings;

        /// <summary>
        /// Execution provider that was successfully initialized.
        /// </summary>
        public OCRExecutionProvider ActiveExecutionProvider { get; private set; }

        /// <summary>
        /// Human-readable name of the active inference backend.
        /// </summary>
        public string ExecutionProviderName =>
            ActiveExecutionProvider == OCRExecutionProvider.OpenVino
                ? "OpenVINO CPU"
                : "ONNX Runtime CPU";

        /// <summary>
        /// Human-readable OCR model version supplied by the model configuration.
        /// </summary>
        public string ModelVersionName { get; private set; }

        // Detection model parameters
        private const int DetMinSize = 3;

        // Recognition model parameters
        private const int RecImgHeight = 48;
        private const int RecImgWidth = 320;

        /// <summary>
        /// Clamp helper method - .NET Framework 4.8 does not contain Math.Clamp
        /// </summary>
        private static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0) return min;
            if (value.CompareTo(max) > 0) return max;
            return value;
        }

        /// <summary>
        /// Safely clone Mat object, avoid AccessViolationException
        /// Use CopyTo as the main method, if it fails, try Clone
        /// </summary>
        private static Mat SafeClone(Mat src)
        {
            if (src == null)
                throw new ArgumentNullException(nameof(src));

            // Check if Mat is disposed
            if (src.IsDisposed)
                throw new ObjectDisposedException(nameof(src), "Mat object has been disposed");

            try
            {
                // Check if Mat is empty
                if (src.Empty())
                {
                    // If empty, return an empty Mat instead of throwing an exception
                    var size = src.Size();
                    var type = src.Type();
                    return new Mat(size, type);
                }

                // Use CopyTo method, it is usually safer than Clone
                // CopyTo will create a new Mat and copy data, without depending on the underlying pointer of the original Mat
                var result = new Mat();
                src.CopyTo(result);
                return result;
            }
            catch (AccessViolationException)
            {
                // If CopyTo fails, try using Clone as a backup solution
                try
                {
                    return src.Clone();
                }
                catch (AccessViolationException ex)
                {
                    // If both methods fail, provide detailed error information
                    string sizeInfo = "Unknown";
                    string typeInfo = "Unknown";
                    try
                    {
                        if (!src.IsDisposed)
                        {
                            sizeInfo = src.Size().ToString();
                            typeInfo = src.Type().ToString();
                        }
                    }
                    catch
                    {
                        Logger.Log.Error($"Failed to clone Mat object: Mat may be corrupted or memory has been released. Size={sizeInfo}, Type={typeInfo}, IsDisposed={src.IsDisposed}");
                    }

                    throw new InvalidOperationException(
                        $"Failed to clone Mat object: Mat may be corrupted or memory has been released. Size={sizeInfo}, Type={typeInfo}, IsDisposed={src.IsDisposed}", ex);
                }
            }
            catch (Exception ex)
            {
                // Skip AccessViolationException, it has already been handled above
                if (ex is AccessViolationException)
                    throw;

                // Handle other types of exceptions (e.g. OutOfMemoryException, etc.)
                string sizeInfo = "Unknown";
                string typeInfo = "Unknown";
                try
                {
                    if (!src.IsDisposed)
                    {
                        sizeInfo = src.Size().ToString();
                        typeInfo = src.Type().ToString();
                    }
                }
                catch
                {
                    Logger.Log.Error(
                        $"Failed to clone Mat object: Mat may be corrupted or memory has been released. Size={sizeInfo}, Type={typeInfo}, IsDisposed={src.IsDisposed} ex = {ex.Message}");
                }

                throw new InvalidOperationException(
                    $"Failed to clone Mat object: {ex.GetType().Name} - {ex.Message}. Size={sizeInfo}, Type={typeInfo}, IsDisposed={src.IsDisposed}", ex);
            }
        }

        /// <summary>
        /// Load character dictionary from YAML file
        /// </summary>
        private static List<string> LoadLabelsFromYaml(string yamlPath)
        {
            var labels = new List<string>();
            var lines = File.ReadAllLines(yamlPath, System.Text.Encoding.UTF8);
            bool inCharacterDict = false;
            var regex = new System.Text.RegularExpressions.Regex(@"^\s*-\s*(.+)");

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("character_dict:"))
                {
                    inCharacterDict = true;
                    continue;
                }
                else if (inCharacterDict)
                {
                    // Use regular expression to match list items
                    var match = regex.Match(line);
                    if (match.Success)
                    {
                        var label = ParseYamlScalar(match.Groups[1].Value.Trim());
                        labels.Add(label);
                    }
                    else if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        // If not a list item, end character dictionary
                        break;
                    }
                }
            }

            if (labels.Count == 0)
            {
                throw new InvalidOperationException($"Failed to read character dictionary from YAML file: {yamlPath}");
            }

            return labels;
        }

        private static string ParseYamlScalar(string value)
        {
            if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
            {
                return value.Substring(1, value.Length - 2).Replace("''", "'");
            }

            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                return value.Substring(1, value.Length - 2)
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\")
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t");
            }

            return value;
        }

        private static DetectionModelSettings LoadDetectionSettings(
            string yamlPath,
            OCRParameter fallback)
        {
            var settings = new DetectionModelSettings
            {
                MaxSideLength = fallback.max_side_len,
                PixelThreshold = fallback.det_db_thresh,
                BoxThreshold = fallback.det_db_box_thresh,
                UnclipRatio = fallback.det_db_unclip_ratio,
                MaxCandidates = 1000,
                UseDilation = fallback.use_dilation
            };

            if (!File.Exists(yamlPath))
                return settings;

            foreach (string line in File.ReadAllLines(yamlPath, System.Text.Encoding.UTF8))
            {
                string trimmed = line.Trim();
                int separatorIndex = trimmed.IndexOf(':');
                if (separatorIndex <= 0)
                    continue;

                string key = trimmed.Substring(0, separatorIndex).Trim();
                string value = ParseYamlScalar(
                    trimmed.Substring(separatorIndex + 1).Trim());
                switch (key)
                {
                    case "resize_long":
                    case "limit_side_len":
                        if (int.TryParse(
                                value,
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out int maxSideLength) &&
                            maxSideLength > 0)
                        {
                            settings.MaxSideLength = maxSideLength;
                        }
                        break;
                    case "thresh":
                        if (TryParseYamlFloat(value, out float pixelThreshold))
                            settings.PixelThreshold = pixelThreshold;
                        break;
                    case "box_thresh":
                        if (TryParseYamlFloat(value, out float boxThreshold))
                            settings.BoxThreshold = boxThreshold;
                        break;
                    case "unclip_ratio":
                        if (TryParseYamlFloat(value, out float unclipRatio))
                            settings.UnclipRatio = unclipRatio;
                        break;
                    case "max_candidates":
                        if (int.TryParse(
                                value,
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out int maxCandidates) &&
                            maxCandidates > 0)
                        {
                            settings.MaxCandidates = maxCandidates;
                        }
                        break;
                    case "use_dilation":
                    case "dilation":
                        if (bool.TryParse(value, out bool useDilation))
                            settings.UseDilation = useDilation;
                        break;
                }
            }

            return settings;
        }

        private static bool TryParseYamlFloat(string value, out float result)
        {
            return float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result);
        }

        /// <summary>
        /// PaddleOCR Engine initialization
        /// </summary>
        /// <param name="config">Model configuration object</param>
        /// <param name="parameter">Recognition parameters</param>
        public PaddleOCREngine(OCRModelConfig config, OCRParameter parameter = null)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (parameter == null)
                parameter = new OCRParameter();
            _parameter = parameter;

            // Check if model files exist
            if (!File.Exists(config.det_infer))
                throw new FileNotFoundException($"Detection model file not found: {config.det_infer}");
            if (!File.Exists(config.rec_infer))
                throw new FileNotFoundException($"Recognition model file not found: {config.rec_infer}");

            var detectionYmlPath = Path.Combine(
                Path.GetDirectoryName(config.det_infer),
                "inference.yml");
            _detectionSettings = LoadDetectionSettings(detectionYmlPath, parameter);

            // Load character dictionary - first from inference.yml, if not, from keys file
            var inferenceYmlPath = Path.Combine(Path.GetDirectoryName(config.rec_infer), "inference.yml");
            if (File.Exists(inferenceYmlPath))
            {
                _labels = LoadLabelsFromYaml(inferenceYmlPath);
            }
            else if (!string.IsNullOrEmpty(config.keys) && File.Exists(config.keys))
            {
                _labels = File.ReadAllLines(config.keys).ToList();
            }
            else
            {
                throw new FileNotFoundException($"Character dictionary file not found: {inferenceYmlPath} or {config.keys}");
            }

            InferenceSession detSession;
            InferenceSession recSession;
            var tryOpenVino = ShouldTryOpenVino(parameter.execution_provider);

            if (tryOpenVino &&
                TryCreateOpenVinoSessions(
                    config.det_infer,
                    config.rec_infer,
                    _detectionSettings.MaxSideLength,
                    parameter.warm_up_openvino,
                    out detSession,
                    out recSession))
            {
                ActiveExecutionProvider = OCRExecutionProvider.OpenVino;
            }
            else
            {
                CreateCpuSessions(config.det_infer, config.rec_infer, out detSession, out recSession);
                ActiveExecutionProvider = OCRExecutionProvider.Cpu;
            }

            _detSession = detSession;
            _recSession = recSession;
            ModelVersionName = string.IsNullOrWhiteSpace(config.model_version)
                ? "Unknown"
                : config.model_version;
            Logger.Log.Info(
                $"OCR model: {ModelVersionName}; execution provider: {ExecutionProviderName}; " +
                $"detector: thresh={_detectionSettings.PixelThreshold}, " +
                $"box_thresh={_detectionSettings.BoxThreshold}, " +
                $"unclip={_detectionSettings.UnclipRatio}, " +
                $"dilation={_detectionSettings.UseDilation}");
        }

        private static bool ShouldTryOpenVino(OCRExecutionProvider requestedProvider)
        {
            if (requestedProvider == OCRExecutionProvider.Cpu)
                return false;

            if (requestedProvider == OCRExecutionProvider.OpenVino)
                return true;

            try
            {
                using (var processorKey = Registry.LocalMachine.OpenSubKey(
                           @"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    var vendor = processorKey?.GetValue("VendorIdentifier") as string;
                    return string.Equals(vendor, "GenuineIntel", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                Logger.Log.Warn($"Unable to detect CPU vendor; using ORT CPU: {ex.Message}");
                return false;
            }
        }

        private static bool TryCreateOpenVinoSessions(
            string detModelPath,
            string recModelPath,
            int detectionMaxSideLength,
            bool warmUp,
            out InferenceSession detSession,
            out InferenceSession recSession)
        {
            detSession = null;
            recSession = null;

            try
            {
                using (var sessionOptions = new SessionOptions())
                {
                    sessionOptions.AppendExecutionProvider_OpenVINO("CPU");
                    detSession = new InferenceSession(detModelPath, sessionOptions);
                    recSession = new InferenceSession(recModelPath, sessionOptions);
                }

                if (warmUp)
                    WarmUpSessions(
                        detSession,
                        recSession,
                        detectionMaxSideLength);

                return true;
            }
            catch (Exception ex)
            {
                detSession?.Dispose();
                recSession?.Dispose();
                detSession = null;
                recSession = null;
                Logger.Log.Warn(
                    $"OpenVINO initialization failed; falling back to ORT CPU for both OCR models: {ex.Message}");
                return false;
            }
        }

        private static void CreateCpuSessions(
            string detModelPath,
            string recModelPath,
            out InferenceSession detSession,
            out InferenceSession recSession)
        {
            detSession = null;
            recSession = null;

            try
            {
                using (var sessionOptions = new SessionOptions())
                {
                    sessionOptions.AppendExecutionProvider_CPU();
                    sessionOptions.IntraOpNumThreads = 2;
                    sessionOptions.InterOpNumThreads = 1;
                    detSession = new InferenceSession(detModelPath, sessionOptions);
                    recSession = new InferenceSession(recModelPath, sessionOptions);
                }
            }
            catch
            {
                detSession?.Dispose();
                recSession?.Dispose();
                throw;
            }
        }

        private static void WarmUpSessions(
            InferenceSession detSession,
            InferenceSession recSession,
            int detectionMaxSideLength)
        {
            var detTensor = new DenseTensor<float>(
                new[] { 1, 3, 320, detectionMaxSideLength });
            var detInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(detSession.InputNames[0], detTensor)
            };
            using (detSession.Run(detInputs))
            {
            }

            var recTensor = new DenseTensor<float>(new[] { 1, 3, RecImgHeight, RecImgWidth });
            var recInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(recSession.InputNames[0], recTensor)
            };
            using (recSession.Run(recInputs))
            {
            }
        }

        /// <summary>
        /// Text recognition for image file
        /// </summary>
        /// <param name="imagefile">Image file</param>
        /// <returns>OCR recognition result</returns>
        public OCRResult DetectText(string imagefile)
        {
            if (!File.Exists(imagefile))
                throw new FileNotFoundException($"File not found: {imagefile}");

            using var image = new Bitmap(imagefile);
            return DetectText(image);
        }

        /// <summary>
        /// Text recognition for image object
        /// </summary>
        /// <param name="image">Image</param>
        /// <returns>OCR recognition result</returns>
        public OCRResult DetectText(Image image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            var bitmap = image as Bitmap;
            if (bitmap == null)
                throw new ArgumentException("Image must be a Bitmap", nameof(image));

            return DetectTextFromMat(bitmap.ToMat());
        }

        /// <summary>
        /// Text recognition for image byte array
        /// </summary>
        /// <param name="imagebyte">Image byte array</param>
        /// <returns>OCR recognition result</returns>
        public OCRResult DetectText(byte[] imagebyte)
        {
            if (imagebyte == null)
                throw new ArgumentNullException(nameof(imagebyte));

            using var ms = new MemoryStream(imagebyte);
            using var image = new Bitmap(ms);
            return DetectText(image);
        }

        /// <summary>
        /// Text recognition for image base64 string
        /// </summary>
        /// <param name="imagebase64">Image base64</param>
        /// <returns>OCR recognition result</returns>
        public OCRResult DetectTextBase64(string imagebase64)
        {
            if (string.IsNullOrEmpty(imagebase64))
                throw new ArgumentNullException(nameof(imagebase64));

            var imageBytes = Convert.FromBase64String(imagebase64);
            return DetectText(imageBytes);
        }

        /// <summary>
        /// 从Mat进行OCR识别
        /// </summary>
        public OCRResult DetectTextFromMat(Mat src)
        {
            if (src == null || src.IsDisposed || src.Empty())
                throw new ArgumentException("Invalid Mat object", nameof(src));

            // Text detection
            var rects = DetectTextRegions(src);

            // Text recognition
            var textBlocks = new List<TextBlock>();
            if (rects.Length > 0)
            {
                var croppedMats = new List<Mat>();
                var validRectIndices = new List<int>(); // Record indices of valid rectangles
                try
                {
                    for (int i = 0; i < rects.Length; i++)
                    {
                        var cropped = GetPerspectiveCrop(src, rects[i]);
                        if (cropped == null || cropped.Empty())
                        {
                            cropped?.Dispose();
                            continue;
                        }

                        croppedMats.Add(cropped);
                        validRectIndices.Add(i); // Record original index of valid rectangles
                    }

                    var results = RecognizeText(croppedMats.ToArray());
                    for (int i = 0; i < results.Count && i < validRectIndices.Count; i++)
                    {
                        var originalIndex = validRectIndices[i];
                        var textBlock = new TextBlock
                        {
                            Text = results[i].Text,
                            Score = results[i].Score,
                            BoxPoints = GetBoxPoints(rects[originalIndex])
                        };
                        textBlocks.Add(textBlock);
                    }
                }
                finally
                {
                    foreach (var mat in croppedMats)
                        mat.Dispose();
                }
            }

            return new OCRResult
            {
                TextBlocks = textBlocks,
                Text = string.Join("\n", textBlocks
                    .Where(tb => tb.Score >= _parameter.rec_score_thresh)
                    .Select(tb => tb.Text))
            };
        }

        /// <summary>
        /// Text detection
        /// </summary>
        private RotatedRect[] DetectTextRegions(Mat src)
        {
            if (src == null || src.IsDisposed || src.Empty())
                throw new ArgumentException("Invalid Mat object", nameof(src));

            using var padded = src.Channels() switch
            {
                4 => src.CvtColor(ColorConversionCodes.BGRA2BGR),
                1 => src.CvtColor(ColorConversionCodes.GRAY2BGR),
                _ => SafeClone(src)
            };

            // Resize
            using var resized = ResizeImage(padded, _detectionSettings.MaxSideLength);
            var resizedSize = new CvSize(resized.Width, resized.Height);
            using var padded32 = PadTo32(resized);

            // Normalize
            var inputTensor = NormalizeImage(padded32);
            using var _ = padded32;

            // Run detection model
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_detSession.InputNames[0], inputTensor)
            };

            using var outputs = _detSession.Run(inputs);
            var output = outputs.First().AsTensor<float>();

            // Convert to Mat
            using var pred = TensorToMat(output);

            // Post-processing
            using var cbuf = new Mat();
            using var roi = pred[new Rect(0, 0, resizedSize.Width, resizedSize.Height)];
            roi.ConvertTo(cbuf, MatType.CV_8UC1, 255);

            using var binary = cbuf.Threshold(
                (int)(_detectionSettings.PixelThreshold * 255),
                255,
                ThresholdTypes.Binary);
            CvPoint[][] contours;
            if (_detectionSettings.UseDilation)
            {
                using var dilated = new Mat();
                using var kernel = Cv2.GetStructuringElement(
                    MorphShapes.Rect,
                    new CvSize(2, 2));
                Cv2.Dilate(binary, dilated, kernel);
                contours = dilated.FindContoursAsArray(
                    RetrievalModes.List,
                    ContourApproximationModes.ApproxSimple);
            }
            else
            {
                contours = binary.FindContoursAsArray(
                    RetrievalModes.List,
                    ContourApproximationModes.ApproxSimple);
            }

            var scaleRate = 1.0 * src.Width / resizedSize.Width;

            var rects = contours
                .Take(_detectionSettings.MaxCandidates)
                .Where(x => GetScore(x, pred) > _detectionSettings.BoxThreshold)
                .Select(Cv2.MinAreaRect)
                .Where(x => x.Size.Width > DetMinSize && x.Size.Height > DetMinSize)
                .Select(rect =>
                {
                    var minEdge = Math.Min(rect.Size.Width, rect.Size.Height);
                    var newSize = new Size2f(
                        (rect.Size.Width + _detectionSettings.UnclipRatio * minEdge) * scaleRate,
                        (rect.Size.Height + _detectionSettings.UnclipRatio * minEdge) * scaleRate);
                    return new RotatedRect(rect.Center * scaleRate, newSize, rect.Angle);
                })
                .ToArray();

            return SortTextRegions(rects);
        }

        /// <summary>
        /// Text recognition
        /// </summary>
        private List<TextRecognitionResult> RecognizeText(Mat[] srcs)
        {
            if (srcs.Length == 0)
                return new List<TextRecognitionResult>();

            var results = new List<TextRecognitionResult>();
            foreach (var src in srcs)
            {
                if (src == null || src.IsDisposed || src.Empty())
                {
                    results.Add(new TextRecognitionResult(string.Empty, 0f));
                    continue;
                }

                using var channel3 = src.Channels() switch
                {
                    4 => src.CvtColor(ColorConversionCodes.BGRA2BGR),
                    1 => src.CvtColor(ColorConversionCodes.GRAY2BGR),
                    _ => SafeClone(src)
                };

                // Resize and normalize
                var ratio = channel3.Width / (double)channel3.Height;
                var resizedW = (int)Math.Ceiling(RecImgHeight * ratio);
                if (resizedW < 16) resizedW = 16;
                using var resized = new Mat();
                Cv2.Resize(channel3, resized, new CvSize(resizedW, RecImgHeight));

                // Normalize to [-1, 1]
                using var blob = CvDnn.BlobFromImage(resized, 2.0 / 255.0, default, new Scalar(127.5, 127.5, 127.5), false, false);

                // Get blob data
                var blobData = new float[blob.Total()];
                Marshal.Copy(blob.Data, blobData, 0, blobData.Length);

                var inputTensor = new DenseTensor<float>(
                    blobData,
                    new[] { 1, resized.Channels(), resized.Rows, resized.Cols });

                // Run recognition model
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(_recSession.InputNames[0], inputTensor)
                };

                using var outputs = _recSession.Run(inputs);
                var output = outputs.First().AsTensor<float>();

                // Decode text
                var text = DecodeText(output);
                results.Add(text);
            }

            return results;
        }

        /// <summary>
        /// Decode recognition result
        /// </summary>
        private TextRecognitionResult DecodeText(Tensor<float> output)
        {
            var dimensions = output.Dimensions;
            var charCount = dimensions[1];
            var labelCount = dimensions[2];

            var text = "";
            var lastIndex = 0;
            var score = 0f;
            var validChars = 0;

            for (var n = 0; n < charCount; n++)
            {
                var maxIdx = 0;
                var maxVal = float.MinValue;

                for (var i = 0; i < labelCount; i++)
                {
                    var val = output[0, n, i];
                    if (val > maxVal)
                    {
                        maxVal = val;
                        maxIdx = i;
                    }
                }

                if (maxIdx > 0 && !(n > 0 && maxIdx == lastIndex))
                {
                    score += maxVal;
                    validChars++;
                    // Index mapping rules:
                    // Index 0 = blank (CTC blank character, skip)
                    // Index 1 to _labels.Count = characters in dictionary (index 1 corresponds to _labels[0])
                    // Index _labels.Count + 1 = space character
                    if (maxIdx <= _labels.Count)
                    {
                        text += _labels[maxIdx - 1];
                    }
                    else if (maxIdx == _labels.Count + 1)
                    {
                        // Handle space character
                        text += " ";
                    }
                    // If index is out of range, skip
                }

                lastIndex = maxIdx;
            }

            float averageScore = validChars > 0 ? score / validChars : 0f;
            return new TextRecognitionResult(text, averageScore);
        }

        private sealed class TextRecognitionResult
        {
            public TextRecognitionResult(string text, float score)
            {
                Text = text;
                Score = score;
            }

            public string Text { get; }
            public float Score { get; }
        }

        private sealed class DetectionModelSettings
        {
            public int MaxSideLength { get; set; }
            public float PixelThreshold { get; set; }
            public float BoxThreshold { get; set; }
            public float UnclipRatio { get; set; }
            public int MaxCandidates { get; set; }
            public bool UseDilation { get; set; }
        }

        private sealed class TextLine
        {
            private float _heightSum;

            public List<RotatedRect> Rects { get; } = new List<RotatedRect>();
            public float CenterY { get; private set; }
            public float AverageHeight => _heightSum / Rects.Count;

            public void Add(RotatedRect rect)
            {
                Rects.Add(rect);
                CenterY = (float)Rects.Average(item => item.Center.Y);
                _heightSum += GetTextRegionHeight(rect);
            }
        }

        /// <summary>
        /// Convert Bitmap to Mat
        /// </summary>
        private Mat BitmapToMat(Bitmap bitmap)
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bmpData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            try
            {
                // Use FromPixelData to create Mat, then immediately clone to ensure independent data copy
                // This can avoid the problem of memory failure after UnlockBits
                using var tempMat = Mat.FromPixelData(bitmap.Height, bitmap.Width, MatType.CV_8UC3, bmpData.Scan0, bmpData.Stride);
                // Create independent data copy
                var mat = new Mat();
                tempMat.CopyTo(mat);
                // Convert BGR to RGB
                Cv2.CvtColor(mat, mat, ColorConversionCodes.BGR2RGB);
                return mat;
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }

        /// <summary>
        /// Resize image
        /// </summary>
        private Mat ResizeImage(Mat src, int maxSize)
        {
            if (src == null || src.IsDisposed || src.Empty())
                throw new ArgumentException("Invalid Mat object", nameof(src));

            var size = src.Size();
            var longEdge = Math.Max(size.Width, size.Height);
            var scaleRate = 1.0 * maxSize / longEdge;
            return scaleRate < 1.0 ? src.Resize(default, scaleRate, scaleRate) : SafeClone(src);
        }

        /// <summary>
        /// Pad to 32's multiple
        /// </summary>
        private Mat PadTo32(Mat src)
        {
            var size = src.Size();
            var newSize = new OpenCvSharp.Size(
                32 * (int)Math.Ceiling(1.0 * size.Width / 32),
                32 * (int)Math.Ceiling(1.0 * size.Height / 32));
            return src.CopyMakeBorder(0, newSize.Height - size.Height, 0, newSize.Width - size.Width, BorderTypes.Constant, Scalar.Black);
        }

        /// <summary>
        /// Normalize image
        /// </summary>
        private Tensor<float> NormalizeImage(Mat src)
        {
            var mean = new[] { 0.485f, 0.456f, 0.406f };
            var std = new[] { 0.229f, 0.224f, 0.225f };
            var scale = 1.0f / 255.0f;

            using var stdMat = new Mat();
            var channels = src.Split();
            try
            {
                for (var i = 0; i < channels.Length; i++)
                {
                    channels[i].ConvertTo(channels[i], MatType.CV_32FC1, scale / std[i], -mean[i] / std[i]);
                }
                Cv2.Merge(channels, stdMat);
            }
            finally
            {
                foreach (var channel in channels)
                    channel.Dispose();
            }

            using var blob = CvDnn.BlobFromImage(stdMat);
            var blobData = new float[blob.Total()];
            Marshal.Copy(blob.Data, blobData, 0, blobData.Length);
            return new DenseTensor<float>(blobData, new[] { 1, 3, stdMat.Rows, stdMat.Cols });
        }

        /// <summary>
        /// Convert Tensor to Mat
        /// </summary>
        private Mat TensorToMat(Tensor<float> tensor)
        {
            var dimensions = tensor.Dimensions;
            if (dimensions.Length != 4 || dimensions[0] != 1 || dimensions[1] != 1)
                throw new ArgumentException($"错误的tensor形状: {string.Join(",", dimensions.ToString())}");

            var data = tensor.ToArray();
            return Mat.FromPixelData(dimensions[2], dimensions[3], MatType.CV_32FC1, data);
        }

        /// <summary>
        /// Get contour score
        /// </summary>
        private float GetScore(CvPoint[] contour, Mat pred)
        {
            var width = pred.Width;
            var height = pred.Height;
            var boxX = contour.Select(v => v.X).ToArray();
            var boxY = contour.Select(v => v.Y).ToArray();

            var xmin = Clamp(boxX.Min(), 0, width - 1);
            var xmax = Clamp(boxX.Max(), 0, width - 1);
            var ymin = Clamp(boxY.Min(), 0, height - 1);
            var ymax = Clamp(boxY.Max(), 0, height - 1);

            var rootPoints = contour.Select(v => new CvPoint(v.X - xmin, v.Y - ymin)).ToArray();
            using var mask = new Mat(ymax - ymin + 1, xmax - xmin + 1, MatType.CV_8UC1, Scalar.Black);
            Cv2.FillPoly(mask, new[] { rootPoints }, new Scalar(1));

            using var croppedMat = pred[new Rect(xmin, ymin, xmax - xmin + 1, ymax - ymin + 1)];
            return (float)croppedMat.Mean(mask).Val0;
        }

        private static RotatedRect[] SortTextRegions(IEnumerable<RotatedRect> rects)
        {
            var lines = new List<TextLine>();
            foreach (RotatedRect rect in rects.OrderBy(item => item.Center.Y))
            {
                float height = GetTextRegionHeight(rect);
                TextLine line = lines
                    .Where(candidate =>
                    {
                        float tolerance = Math.Max(
                            10f,
                            Math.Min(candidate.AverageHeight, height) * 0.5f);
                        return Math.Abs(candidate.CenterY - rect.Center.Y) <= tolerance;
                    })
                    .OrderBy(candidate => Math.Abs(candidate.CenterY - rect.Center.Y))
                    .FirstOrDefault();

                if (line == null)
                {
                    line = new TextLine();
                    lines.Add(line);
                }

                line.Add(rect);
            }

            return lines
                .OrderBy(line => line.CenterY)
                .SelectMany(line => line.Rects.OrderBy(rect => rect.Center.X))
                .ToArray();
        }

        private static float GetTextRegionHeight(RotatedRect rect)
        {
            return Math.Max(1f, Math.Min(rect.Size.Width, rect.Size.Height));
        }

        private static Mat GetPerspectiveCrop(Mat source, RotatedRect rect)
        {
            OpenCvSharp.Point2f[] points = OrderClockwise(rect.Points());
            int width = Math.Max(
                1,
                (int)Math.Round(Math.Max(
                    Distance(points[0], points[1]),
                    Distance(points[3], points[2]))));
            int height = Math.Max(
                1,
                (int)Math.Round(Math.Max(
                    Distance(points[0], points[3]),
                    Distance(points[1], points[2]))));
            var destination = new[]
            {
                new OpenCvSharp.Point2f(0, 0),
                new OpenCvSharp.Point2f(width - 1, 0),
                new OpenCvSharp.Point2f(width - 1, height - 1),
                new OpenCvSharp.Point2f(0, height - 1)
            };

            using var transform = Cv2.GetPerspectiveTransform(points, destination);
            var cropped = new Mat();
            Cv2.WarpPerspective(
                source,
                cropped,
                transform,
                new CvSize(width, height),
                InterpolationFlags.Cubic,
                BorderTypes.Replicate);

            if (cropped.Rows >= cropped.Cols * 1.5)
            {
                var rotated = new Mat();
                Cv2.Rotate(cropped, rotated, RotateFlags.Rotate90Counterclockwise);
                cropped.Dispose();
                return rotated;
            }

            return cropped;
        }

        private static OpenCvSharp.Point2f[] OrderClockwise(
            IEnumerable<OpenCvSharp.Point2f> points)
        {
            OpenCvSharp.Point2f[] array = points.ToArray();
            float centerX = array.Average(point => point.X);
            float centerY = array.Average(point => point.Y);
            OpenCvSharp.Point2f[] clockwise = array
                .OrderBy(point => Math.Atan2(point.Y - centerY, point.X - centerX))
                .ToArray();
            int topLeftIndex = Enumerable.Range(0, clockwise.Length)
                .OrderBy(index => clockwise[index].X + clockwise[index].Y)
                .First();
            return Enumerable.Range(0, clockwise.Length)
                .Select(offset => clockwise[(topLeftIndex + offset) % clockwise.Length])
                .ToArray();
        }

        private static double Distance(
            OpenCvSharp.Point2f first,
            OpenCvSharp.Point2f second)
        {
            double deltaX = first.X - second.X;
            double deltaY = first.Y - second.Y;
            return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        }

        /// <summary>
        /// Get four corners of rotated rectangle
        /// </summary>
        private PointF[] GetBoxPoints(RotatedRect rect)
        {
            unsafe
            {
                var points = rect.Points();
                var result = new PointF[4];
                for (int i = 0; i < 4; i++)
                {
                    result[i] = new PointF(points[i].X, points[i].Y);
                }
                return result;
            }
        }

        /// <summary>
        /// Release resources
        /// </summary>
        public void Dispose()
        {
            _detSession?.Dispose();
            _recSession?.Dispose();
        }
    }
}
