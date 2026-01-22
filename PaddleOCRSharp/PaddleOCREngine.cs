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
using CvPoint = OpenCvSharp.Point;
using CvSize = OpenCvSharp.Size;
using log4net;

namespace PaddleOCRSharp
{
    /// <summary>
    /// PaddleOCR识别引擎对象 - 使用ONNX Runtime实现
    /// 兼容.NET Framework 4.8
    /// </summary>
    public class PaddleOCREngine : IDisposable
    {
        private readonly InferenceSession _detSession;
        private readonly InferenceSession _recSession;
        private readonly List<string> _labels;
        private readonly OCRParameter _parameter;

        // 检测模型参数
        private const int DetMaxSize = 960;
        private const float DetBoxScoreThreshold = 0.7f;
        private const float DetBoxThreshold = 0.3f;
        private const int DetMinSize = 3;
        private const float DetUnclipRatio = 2.0f;

        // 识别模型参数
        private const int RecImgHeight = 48;
        private const int RecImgWidth = 320;

        /// <summary>
        /// Clamp辅助方法 - .NET Framework 4.8不包含Math.Clamp
        /// </summary>
        private static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0) return min;
            if (value.CompareTo(max) > 0) return max;
            return value;
        }

        /// <summary>
        /// 安全地克隆Mat对象，避免AccessViolationException
        /// 使用CopyTo作为主要方法，如果失败则尝试Clone
        /// </summary>
        private static Mat SafeClone(Mat src)
        {
            if (src == null)
                throw new ArgumentNullException(nameof(src));

            // 检查Mat是否已释放
            if (src.IsDisposed)
                throw new ObjectDisposedException(nameof(src), "Mat对象已被释放");

            try
            {
                // 检查Mat是否为空
                if (src.Empty())
                {
                    // 如果为空，返回一个空的Mat而不是抛出异常
                    var size = src.Size();
                    var type = src.Type();
                    return new Mat(size, type);
                }

                // 优先使用CopyTo方法，它通常比Clone更安全
                // CopyTo会创建新的Mat并复制数据，不依赖原始Mat的底层指针
                var result = new Mat();
                src.CopyTo(result);
                return result;
            }
            catch (AccessViolationException)
            {
                // 如果CopyTo失败，尝试使用Clone作为后备方案
                try
                {
                    return src.Clone();
                }
                catch (AccessViolationException ex)
                {
                    // 如果两种方法都失败，提供详细的错误信息
                    string sizeInfo = "未知";
                    string typeInfo = "未知";
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
                        Logger.Log.Error($"无法克隆Mat对象: Mat可能已损坏或内存已释放。Size={sizeInfo}, Type={typeInfo}, IsDisposed={src.IsDisposed}");
                    }

                    throw new InvalidOperationException(
                        $"无法克隆Mat对象: Mat可能已损坏或内存已释放。Size={sizeInfo}, Type={typeInfo}, IsDisposed={src.IsDisposed}", ex);
                }
            }
            catch (Exception ex)
            {
                // 跳过AccessViolationException，因为它已经在上面处理了
                if (ex is AccessViolationException)
                    throw;

                // 处理其他类型的异常（如OutOfMemoryException等）
                string sizeInfo = "未知";
                string typeInfo = "未知";
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
                        $"无法克隆Mat对象: Mat可能已损坏或内存已释放。Size={sizeInfo}, Type={typeInfo}, IsDisposed={src.IsDisposed} ex = {ex.Message}");
                }

                throw new InvalidOperationException(
                    $"无法克隆Mat对象: {ex.GetType().Name} - {ex.Message}。Size={sizeInfo}, Type={typeInfo}, IsDisposed={src.IsDisposed}", ex);
            }
        }

        /// <summary>
        /// 从YAML文件中加载字符字典
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
                    // 使用正则表达式匹配列表项
                    var match = regex.Match(line);
                    if (match.Success)
                    {
                        var label = match.Groups[1].Value.Trim();
                        labels.Add(label);
                    }
                    else if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        // 遇到非列表项，结束字符字典
                        break;
                    }
                }
            }

            if (labels.Count == 0)
            {
                throw new InvalidOperationException($"无法从YAML文件中读取字符字典: {yamlPath}");
            }

            return labels;
        }

        /// <summary>
        /// PaddleOCR识别引擎对象初始化
        /// </summary>
        /// <param name="config">模型配置对象</param>
        /// <param name="parameter">识别参数</param>
        public PaddleOCREngine(OCRModelConfig config, OCRParameter parameter = null)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (parameter == null)
                parameter = new OCRParameter();
            _parameter = parameter;

            // 检查模型文件是否存在
            if (!File.Exists(config.det_infer))
                throw new FileNotFoundException($"检测模型文件不存在: {config.det_infer}");
            if (!File.Exists(config.rec_infer))
                throw new FileNotFoundException($"识别模型文件不存在: {config.rec_infer}");

            // 加载字符字典 - 优先从inference.yml读取，如果没有则从keys文件读取
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
                throw new FileNotFoundException($"字符字典文件不存在: {inferenceYmlPath} 或 {config.keys}");
            }

            // 创建ONNX Runtime会话
            var sessionOptions = new SessionOptions();
            sessionOptions.AppendExecutionProvider_CPU();
            sessionOptions.IntraOpNumThreads = 2;
            sessionOptions.InterOpNumThreads = 1;

            _detSession = new InferenceSession(config.det_infer, sessionOptions);
            _recSession = new InferenceSession(config.rec_infer, sessionOptions);
        }

        /// <summary>
        /// 对图像文件进行文本识别
        /// </summary>
        /// <param name="imagefile">图像文件</param>
        /// <returns>OCR识别结果</returns>
        public OCRResult DetectText(string imagefile)
        {
            if (!File.Exists(imagefile))
                throw new FileNotFoundException($"文件不存在: {imagefile}");

            using var image = new Bitmap(imagefile);
            return DetectText(image);
        }

        /// <summary>
        /// 对图像对象进行文本识别
        /// </summary>
        /// <param name="image">图像</param>
        /// <returns>OCR识别结果</returns>
        public OCRResult DetectText(Image image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            var bitmap = image as Bitmap;
            if (bitmap == null)
                throw new ArgumentException("Image must be a Bitmap", nameof(image));

            using var mat = BitmapToMat(bitmap);
            return DetectTextFromMat(mat);
        }

        /// <summary>
        /// 对图像字节数组进行文本识别
        /// </summary>
        /// <param name="imagebyte">图像字节数组</param>
        /// <returns>OCR识别结果</returns>
        public OCRResult DetectText(byte[] imagebyte)
        {
            if (imagebyte == null)
                throw new ArgumentNullException(nameof(imagebyte));

            using var ms = new MemoryStream(imagebyte);
            using var image = new Bitmap(ms);
            return DetectText(image);
        }

        /// <summary>
        /// 对图像base64字符串进行文本识别
        /// </summary>
        /// <param name="imagebase64">图像base64</param>
        /// <returns>OCR识别结果</returns>
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
                throw new ArgumentException("输入的Mat对象无效", nameof(src));

            // 文本检测
            var rects = DetectTextRegions(src);

            // 文本识别
            var textBlocks = new List<TextBlock>();
            if (rects.Length > 0)
            {
                var croppedMats = new List<Mat>();
                var validRectIndices = new List<int>(); // 记录有效矩形的索引
                try
                {
                    var srcSize = src.Size();
                    for (int i = 0; i < rects.Length; i++)
                    {
                        var rect = rects[i];
                        var croppedRect = GetCroppedRect(rect.BoundingRect(), srcSize);

                        // 额外的安全检查：确保矩形在Mat边界内
                        if (croppedRect.X < 0 || croppedRect.Y < 0 ||
                            croppedRect.X + croppedRect.Width > srcSize.Width ||
                            croppedRect.Y + croppedRect.Height > srcSize.Height ||
                            croppedRect.Width <= 0 || croppedRect.Height <= 0)
                        {
                            // 如果矩形无效，跳过这个区域
                            continue;
                        }

                        var roi = src[croppedRect];
                        croppedMats.Add(roi);
                        validRectIndices.Add(i); // 记录有效矩形的原始索引
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
        /// 文本检测
        /// </summary>
        private RotatedRect[] DetectTextRegions(Mat src)
        {
            if (src == null || src.IsDisposed || src.Empty())
                throw new ArgumentException("输入的Mat对象无效", nameof(src));

            using var padded = src.Channels() switch
            {
                4 => src.CvtColor(ColorConversionCodes.BGRA2BGR),
                1 => src.CvtColor(ColorConversionCodes.GRAY2BGR),
                _ => SafeClone(src)
            };

            // 调整大小
            using var resized = ResizeImage(padded, DetMaxSize);
            var resizedSize = new CvSize(resized.Width, resized.Height);
            using var padded32 = PadTo32(resized);

            // 归一化
            var inputTensor = NormalizeImage(padded32);
            using var _ = padded32;

            // 运行检测模型
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_detSession.InputNames[0], inputTensor)
            };

            using var outputs = _detSession.Run(inputs);
            var output = outputs.First().AsTensor<float>();

            // 转换为Mat
            using var pred = TensorToMat(output);

            // 后处理
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
        /// 文本识别
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

                // 调整大小并归一化
                var ratio = channel3.Width / (double)channel3.Height;
                var resizedW = (int)Math.Ceiling(RecImgHeight * ratio);
                if (resizedW < 16) resizedW = 16;
                using var resized = new Mat();
                Cv2.Resize(channel3, resized, new CvSize(resizedW, RecImgHeight));

                // 归一化到[-1, 1]
                using var blob = CvDnn.BlobFromImage(resized, 2.0 / 255.0, default, new Scalar(127.5, 127.5, 127.5), false, false);

                // 获取blob数据
                var blobData = new float[blob.Total()];
                Marshal.Copy(blob.Data, blobData, 0, blobData.Length);

                var inputTensor = new DenseTensor<float>(
                    blobData,
                    new[] { 1, resized.Channels(), resized.Rows, resized.Cols });

                // 运行识别模型
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(_recSession.InputNames[0], inputTensor)
                };

                using var outputs = _recSession.Run(inputs);
                var output = outputs.First().AsTensor<float>();

                // 解码文本
                var text = DecodeText(output);
                results.Add(text);
            }

            return results;
        }

        /// <summary>
        /// 解码识别结果
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
                    // 索引映射规则：
                    // 索引 0 = blank（CTC空白符，跳过）
                    // 索引 1 到 _labels.Count = 字典中的字符（索引1对应_labels[0]）
                    // 索引 _labels.Count + 1 = 空格字符
                    if (maxIdx <= _labels.Count)
                    {
                        text += _labels[maxIdx - 1];
                    }
                    else if (maxIdx == _labels.Count + 1)
                    {
                        // 处理空格字符（参考 better-genshin-impact 的实现）
                        text += " ";
                    }
                    // 如果索引超出范围，跳过（可能是模型输出异常）
                }

                lastIndex = maxIdx;
            }

            return text;
        }

        /// <summary>
        /// Bitmap转Mat
        /// </summary>
        private Mat BitmapToMat(Bitmap bitmap)
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bmpData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            try
            {
                // 使用FromPixelData创建Mat，然后立即克隆以确保拥有独立的数据副本
                // 这样可以避免在UnlockBits后内存失效的问题
                using var tempMat = Mat.FromPixelData(bitmap.Height, bitmap.Width, MatType.CV_8UC3, bmpData.Scan0, bmpData.Stride);
                // 创建独立的数据副本
                var mat = new Mat();
                tempMat.CopyTo(mat);
                // 转换BGR到RGB
                Cv2.CvtColor(mat, mat, ColorConversionCodes.BGR2RGB);
                return mat;
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }

        /// <summary>
        /// 调整图像大小
        /// </summary>
        private Mat ResizeImage(Mat src, int maxSize)
        {
            if (src == null || src.IsDisposed || src.Empty())
                throw new ArgumentException("输入的Mat对象无效", nameof(src));

            var size = src.Size();
            var longEdge = Math.Max(size.Width, size.Height);
            var scaleRate = 1.0 * maxSize / longEdge;
            return scaleRate < 1.0 ? src.Resize(default, scaleRate, scaleRate) : SafeClone(src);
        }

        /// <summary>
        /// 填充到32的倍数
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
        /// 归一化图像
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
        /// Tensor转Mat
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
        /// 获取轮廓得分
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
        /// 获取裁剪区域，确保不超出Mat边界
        /// </summary>
        private Rect GetCroppedRect(Rect rect, CvSize size)
        {
            // 确保起始坐标在有效范围内
            var x = Clamp(rect.X, 0, size.Width - 1);
            var y = Clamp(rect.Y, 0, size.Height - 1);

            // 计算最大可用的宽度和高度
            var maxWidth = size.Width - x;
            var maxHeight = size.Height - y;

            // 确保宽度和高度在有效范围内，并且不会超出边界
            var width = Clamp(rect.Width, 1, maxWidth);
            var height = Clamp(rect.Height, 1, maxHeight);

            // 最终验证：确保 X + Width <= size.Width 和 Y + Height <= size.Height
            if (x + width > size.Width)
                width = size.Width - x;
            if (y + height > size.Height)
                height = size.Height - y;

            // 确保宽度和高度至少为1
            if (width < 1) width = 1;
            if (height < 1) height = 1;

            return new Rect(x, y, width, height);
        }

        /// <summary>
        /// 获取旋转矩形的四个角点
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
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _detSession?.Dispose();
            _recSession?.Dispose();
        }
    }
}