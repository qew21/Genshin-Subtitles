using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

        // Detection model parameters
        private const int DetMaxSize = 960;
        private const float DetBoxScoreThreshold = 0.7f;
        private const float DetBoxThreshold = 0.3f;
        private const int DetMinSize = 3;
        private const float DetUnclipRatio = 2.0f;

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
                        var label = match.Groups[1].Value.Trim();
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

            // Create ONNX Runtime session
            var sessionOptions = new SessionOptions();
            sessionOptions.AppendExecutionProvider_CPU();
            sessionOptions.IntraOpNumThreads = 2;
            sessionOptions.InterOpNumThreads = 1;

            _detSession = new InferenceSession(config.det_infer, sessionOptions);
            _recSession = new InferenceSession(config.rec_infer, sessionOptions);
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
                    var srcSize = src.Size();
                    for (int i = 0; i < rects.Length; i++)
                    {
                        var rect = rects[i];
                        var croppedRect = GetCroppedRect(rect.BoundingRect(), srcSize);

                        // Additional safety check: ensure rectangle is within Mat boundaries
                        if (croppedRect.X < 0 || croppedRect.Y < 0 ||
                            croppedRect.X + croppedRect.Width > srcSize.Width ||
                            croppedRect.Y + croppedRect.Height > srcSize.Height ||
                            croppedRect.Width <= 0 || croppedRect.Height <= 0)
                        {
                            // If rectangle is invalid, skip this region
                            continue;
                        }

                        var roi = src[croppedRect];
                        croppedMats.Add(roi);
                        validRectIndices.Add(i); // Record original index of valid rectangles
                    }

                    var results = RecognizeText(croppedMats.ToArray());
                    for (int i = 0; i < results.Count && i < validRectIndices.Count; i++)
                    {
                        var originalIndex = validRectIndices[i];
                        var textBlock = new TextBlock
                        {
                            Text = results[i],
                            Score = 1.0f,
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
                Text = string.Join("\n", textBlocks.Select(tb => tb.Text))
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
            using var resized = ResizeImage(padded, DetMaxSize);
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

            using var binary = cbuf.Threshold((int)(DetBoxThreshold * 255), 255, ThresholdTypes.Binary);
            using var dilated = new Mat();
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new CvSize(2, 2));
            Cv2.Dilate(binary, dilated, kernel);

            var contours = dilated.FindContoursAsArray(RetrievalModes.List, ContourApproximationModes.ApproxSimple);
            var scaleRate = 1.0 * src.Width / resizedSize.Width;

            var rects = contours
                .Where(x => GetScore(x, pred) > DetBoxScoreThreshold)
                .Select(Cv2.MinAreaRect)
                .Where(x => x.Size.Width > DetMinSize && x.Size.Height > DetMinSize)
                .Select(rect =>
                {
                    var minEdge = Math.Min(rect.Size.Width, rect.Size.Height);
                    var newSize = new Size2f(
                        (rect.Size.Width + DetUnclipRatio * minEdge) * scaleRate,
                        (rect.Size.Height + DetUnclipRatio * minEdge) * scaleRate);
                    return new RotatedRect(rect.Center * scaleRate, newSize, rect.Angle);
                })
                .OrderBy(v => v.Center.Y)
                .ThenBy(v => v.Center.X)
                .ToArray();

            return rects;
        }

        /// <summary>
        /// Text recognition
        /// </summary>
        private List<string> RecognizeText(Mat[] srcs)
        {
            if (srcs.Length == 0)
                return new List<string>();

            var results = new List<string>();
            foreach (var src in srcs)
            {
                if (src == null || src.IsDisposed || src.Empty())
                {
                    results.Add(string.Empty);
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
        private string DecodeText(Tensor<float> output)
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

            return text;
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

        /// <summary>
        /// Get cropped region, ensure it does not exceed Mat boundaries
        /// </summary>
        private Rect GetCroppedRect(Rect rect, CvSize size)
        {
            // Ensure starting coordinates are within valid range
            var x = Clamp(rect.X, 0, size.Width - 1);
            var y = Clamp(rect.Y, 0, size.Height - 1);

            // Calculate maximum available width and height
            var maxWidth = size.Width - x;
            var maxHeight = size.Height - y;

            // Ensure width and height are within valid range, and do not exceed boundaries
            var width = Clamp(rect.Width, 1, maxWidth);
            var height = Clamp(rect.Height, 1, maxHeight);

            // Final validation: ensure X + Width <= size.Width and Y + Height <= size.Height
            if (x + width > size.Width)
                width = size.Width - x;
            if (y + height > size.Height)
                height = size.Height - y;

            // Ensure width and height are at least 1
            if (width < 1) width = 1;
            if (height < 1) height = 1;

            return new Rect(x, y, width, height);
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