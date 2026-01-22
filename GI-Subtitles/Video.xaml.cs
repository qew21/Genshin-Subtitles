using OpenCvSharp;
using PaddleOCRSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Newtonsoft.Json;

namespace GI_Subtitles
{
    /// <summary>
    /// Video.xaml 的交互逻辑
    /// </summary>
    public partial class Video : System.Windows.Window
    {
        private string _videoPath = null;
        private System.Drawing.Size _videoResolution;
        private System.Windows.Point _startPoint;
        private bool _isSelecting = false;
        private bool _isMoving = false;
        private bool _isResizing = false;
        private ResizeHandle _resizeHandle = ResizeHandle.None;
        private System.Windows.Point _lastMousePos;
        private System.Windows.Rect _imageBounds; // 图像在Canvas中的实际显示区域
        private double _currentTimeSeconds = 0; // 当前显示的时间点（秒）
        private double _videoFps = 0; // 视频帧率
        PaddleOCREngine engine;

        // 存储用户选择的区域（GDI Rectangle）
        public System.Drawing.Rectangle SelectedRegion { get; private set; }

        // 选区信息类，用于JSON序列化
        private class RegionInfo
        {
            public string VideoPath { get; set; }
            public string TimeCode { get; set; } // 格式: HH:MM:SS 或 MM:SS
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int VideoWidth { get; set; }
            public int VideoHeight { get; set; }
        }

        private enum ResizeHandle
        {
            None,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight,
            Top,
            Bottom,
            Left,
            Right
        }

        public Video(PaddleOCREngine _engine)
        {
            engine = _engine;
            InitializeComponent();

            // 计算图像边界
            PreviewImage.Loaded += (s, e) => UpdateImageBounds();
            PreviewImage.SizeChanged += (s, e) => UpdateImageBounds();
        }

        private void UpdateImageBounds()
        {
            if (PreviewImage.Source == null) return;

            var source = PreviewImage.Source as BitmapSource;
            if (source == null) return;

            // 计算图像在Image控件中的实际显示区域（考虑Stretch="Uniform"）
            double scale = Math.Min(
                PreviewImage.ActualWidth / source.PixelWidth,
                PreviewImage.ActualHeight / source.PixelHeight);

            double renderedWidth = source.PixelWidth * scale;
            double renderedHeight = source.PixelHeight * scale;
            double offsetX = (PreviewImage.ActualWidth - renderedWidth) / 2;
            double offsetY = (PreviewImage.ActualHeight - renderedHeight) / 2;

            _imageBounds = new System.Windows.Rect(
                offsetX,
                offsetY,
                renderedWidth,
                renderedHeight);
        }

        private void OpenVideo_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "视频文件|*.mp4;*.avi;*.mov;*.mkv|所有文件|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                _videoPath = dialog.FileName;
                LoadFrameAtTime(_videoPath, 0);
                JumpToTime.IsEnabled = true;
                LoadRegion.IsEnabled = true;

                // 自动尝试加载已保存的选区信息
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    TryAutoLoadRegion();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void LoadFrameAtTime(string videoPath, double timeSeconds)
        {
            try
            {
                using var capture = new VideoCapture(videoPath);
                if (!capture.IsOpened())
                    throw new InvalidOperationException("无法打开视频，请尝试转码为 .avi 格式。");

                _videoResolution = new System.Drawing.Size(
                    (int)capture.FrameWidth,
                    (int)capture.FrameHeight);

                _videoFps = capture.Fps;
                if (_videoFps <= 0) _videoFps = 30; // 默认帧率

                // 跳转到指定时间
                double totalDuration = capture.Get(VideoCaptureProperties.FrameCount) / _videoFps;
                timeSeconds = Math.Max(0, Math.Min(timeSeconds, totalDuration));
                _currentTimeSeconds = timeSeconds;

                // 使用毫秒跳转（更准确）
                capture.Set(VideoCaptureProperties.PosMsec, timeSeconds * 1000);

                using var mat = new Mat();
                capture.Read(mat); // 读取当前帧

                if (mat.Empty())
                    throw new Exception("无法读取视频帧。");

                // 转为 BitmapSource 用于 WPF 显示
                var bitmapSource = MatToBitmapSource(mat);
                PreviewImage.Source = (BitmapSource)bitmapSource;
                SelectionCanvas.Visibility = Visibility.Visible;

                // 更新当前时间显示
                UpdateCurrentTimeDisplay();

                // 清除之前的选区
                ClearSelection();

                // 更新图像边界（等待布局完成）
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateImageBounds();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载视频失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateCurrentTimeDisplay()
        {
            var timeSpan = TimeSpan.FromSeconds(_currentTimeSeconds);
            if (timeSpan.TotalHours >= 1)
            {
                CurrentTimeText.Text = $"当前时间: {timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            }
            else
            {
                CurrentTimeText.Text = $"当前时间: {timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            }
        }

        private double ParseTimeString(string timeStr)
        {
            if (string.IsNullOrWhiteSpace(timeStr))
                return 0;

            // 支持格式: MM:SS 或 HH:MM:SS
            var parts = timeStr.Split(':');
            if (parts.Length == 2)
            {
                // MM:SS
                if (int.TryParse(parts[0], out int minutes) && int.TryParse(parts[1], out int seconds))
                {
                    return minutes * 60 + seconds;
                }
            }
            else if (parts.Length == 3)
            {
                // HH:MM:SS
                if (int.TryParse(parts[0], out int hours) &&
                    int.TryParse(parts[1], out int minutes) &&
                    int.TryParse(parts[2], out int seconds))
                {
                    return hours * 3600 + minutes * 60 + seconds;
                }
            }

            throw new FormatException("时间格式错误，请使用 MM:SS 或 HH:MM:SS 格式");
        }

        private string FormatTimeString(double seconds)
        {
            var timeSpan = TimeSpan.FromSeconds(seconds);
            if (timeSpan.TotalHours >= 1)
            {
                return $"{timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            }
            else
            {
                return $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            }
        }

        private void JumpToTime_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_videoPath))
            {
                MessageBox.Show("请先打开视频文件", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                double timeSeconds = ParseTimeString(TimeInput.Text);
                LoadFrameAtTime(_videoPath, timeSeconds);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"跳转失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SelectionCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(SelectionCanvas);

            // 检查是否点击在调整手柄上
            _resizeHandle = GetResizeHandle(pos);
            if (_resizeHandle != ResizeHandle.None)
            {
                _isResizing = true;
                _lastMousePos = pos;
                e.Handled = true;
                return;
            }

            // 检查是否点击在选区内
            if (SelectionRect.Visibility == Visibility.Visible)
            {
                var rect = new System.Windows.Rect(
                    Canvas.GetLeft(SelectionRect),
                    Canvas.GetTop(SelectionRect),
                    SelectionRect.Width,
                    SelectionRect.Height);

                if (rect.Contains(pos))
                {
                    _isMoving = true;
                    _startPoint = pos;
                    _lastMousePos = pos;
                    e.Handled = true;
                    return;
                }
            }

            // 开始创建新选区
            if (_imageBounds.Contains(pos))
            {
                _isSelecting = true;
                _startPoint = pos;
                SelectionRect.Visibility = Visibility.Visible;
                Canvas.SetLeft(SelectionRect, pos.X);
                Canvas.SetTop(SelectionRect, pos.Y);
                SelectionRect.Width = 0;
                SelectionRect.Height = 0;
                UpdateHandles();
            }
        }

        private void SelectionCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            var current = e.GetPosition(SelectionCanvas);

            // 更新坐标信息显示
            UpdateInfoDisplay(current);

            if (_isResizing)
            {
                ResizeSelection(current);
            }
            else if (_isMoving)
            {
                MoveSelection(current);
            }
            else if (_isSelecting)
            {
                CreateSelection(current);
            }
            else if (SelectionRect.Visibility == Visibility.Visible)
            {
                // 更新鼠标光标
                var handle = GetResizeHandle(current);
                UpdateCursor(handle);
            }
        }

        private void SelectionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSelecting || _isMoving || _isResizing)
            {
                _isSelecting = false;
                _isMoving = false;
                _isResizing = false;
                _resizeHandle = ResizeHandle.None;

                // 验证选区是否有效
                if (SelectionRect.Width < 5 || SelectionRect.Height < 5)
                {
                    ClearSelection();
                }
                else
                {
                    // 确保选区在图像范围内
                    ConstrainSelectionToImage();
                    UpdateHandles();
                    Confirm.IsEnabled = true;
                    Clear.IsEnabled = true;
                }
            }
        }

        private void CreateSelection(System.Windows.Point current)
        {
            // 限制在图像区域内
            current.X = Math.Max(_imageBounds.Left, Math.Min(_imageBounds.Right, current.X));
            current.Y = Math.Max(_imageBounds.Top, Math.Min(_imageBounds.Bottom, current.Y));

            double x = Math.Min(_startPoint.X, current.X);
            double y = Math.Min(_startPoint.Y, current.Y);
            double width = Math.Abs(current.X - _startPoint.X);
            double height = Math.Abs(current.Y - _startPoint.Y);

            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);
            SelectionRect.Width = width;
            SelectionRect.Height = height;

            UpdateHandles();
        }

        private void MoveSelection(System.Windows.Point current)
        {
            var deltaX = current.X - _lastMousePos.X;
            var deltaY = current.Y - _lastMousePos.Y;

            double newLeft = Canvas.GetLeft(SelectionRect) + deltaX;
            double newTop = Canvas.GetTop(SelectionRect) + deltaY;

            // 限制在图像区域内
            newLeft = Math.Max(_imageBounds.Left, Math.Min(newLeft, _imageBounds.Right - SelectionRect.Width));
            newTop = Math.Max(_imageBounds.Top, Math.Min(newTop, _imageBounds.Bottom - SelectionRect.Height));

            Canvas.SetLeft(SelectionRect, newLeft);
            Canvas.SetTop(SelectionRect, newTop);

            _lastMousePos = current;
            UpdateHandles();
        }

        private void ResizeSelection(System.Windows.Point current)
        {
            // 限制在图像区域内
            current.X = Math.Max(_imageBounds.Left, Math.Min(_imageBounds.Right, current.X));
            current.Y = Math.Max(_imageBounds.Top, Math.Min(_imageBounds.Bottom, current.Y));

            double left = Canvas.GetLeft(SelectionRect);
            double top = Canvas.GetTop(SelectionRect);
            double width = SelectionRect.Width;
            double height = SelectionRect.Height;
            double right = left + width;
            double bottom = top + height;

            switch (_resizeHandle)
            {
                case ResizeHandle.TopLeft:
                    left = current.X;
                    top = current.Y;
                    break;
                case ResizeHandle.TopRight:
                    right = current.X;
                    top = current.Y;
                    break;
                case ResizeHandle.BottomLeft:
                    left = current.X;
                    bottom = current.Y;
                    break;
                case ResizeHandle.BottomRight:
                    right = current.X;
                    bottom = current.Y;
                    break;
                case ResizeHandle.Top:
                    top = current.Y;
                    break;
                case ResizeHandle.Bottom:
                    bottom = current.Y;
                    break;
                case ResizeHandle.Left:
                    left = current.X;
                    break;
                case ResizeHandle.Right:
                    right = current.X;
                    break;
            }

            // 确保最小尺寸
            if (right - left < 10) right = left + 10;
            if (bottom - top < 10) bottom = top + 10;

            // 限制在图像区域内
            left = Math.Max(_imageBounds.Left, Math.Min(left, _imageBounds.Right - 10));
            top = Math.Max(_imageBounds.Top, Math.Min(top, _imageBounds.Bottom - 10));
            right = Math.Max(left + 10, Math.Min(right, _imageBounds.Right));
            bottom = Math.Max(top + 10, Math.Min(bottom, _imageBounds.Bottom));

            Canvas.SetLeft(SelectionRect, left);
            Canvas.SetTop(SelectionRect, top);
            SelectionRect.Width = right - left;
            SelectionRect.Height = bottom - top;

            _lastMousePos = current;
            UpdateHandles();
        }

        private ResizeHandle GetResizeHandle(System.Windows.Point pos)
        {
            if (SelectionRect.Visibility != Visibility.Visible) return ResizeHandle.None;

            double left = Canvas.GetLeft(SelectionRect);
            double top = Canvas.GetTop(SelectionRect);
            double right = left + SelectionRect.Width;
            double bottom = top + SelectionRect.Height;

            const double handleSize = 12; // 手柄检测区域

            // 检查各个手柄
            if (Math.Abs(pos.X - left) < handleSize && Math.Abs(pos.Y - top) < handleSize)
                return ResizeHandle.TopLeft;
            if (Math.Abs(pos.X - right) < handleSize && Math.Abs(pos.Y - top) < handleSize)
                return ResizeHandle.TopRight;
            if (Math.Abs(pos.X - left) < handleSize && Math.Abs(pos.Y - bottom) < handleSize)
                return ResizeHandle.BottomLeft;
            if (Math.Abs(pos.X - right) < handleSize && Math.Abs(pos.Y - bottom) < handleSize)
                return ResizeHandle.BottomRight;
            if (Math.Abs(pos.Y - top) < handleSize && pos.X >= left && pos.X <= right)
                return ResizeHandle.Top;
            if (Math.Abs(pos.Y - bottom) < handleSize && pos.X >= left && pos.X <= right)
                return ResizeHandle.Bottom;
            if (Math.Abs(pos.X - left) < handleSize && pos.Y >= top && pos.Y <= bottom)
                return ResizeHandle.Left;
            if (Math.Abs(pos.X - right) < handleSize && pos.Y >= top && pos.Y <= bottom)
                return ResizeHandle.Right;

            return ResizeHandle.None;
        }

        private void UpdateCursor(ResizeHandle handle)
        {
            switch (handle)
            {
                case ResizeHandle.TopLeft:
                case ResizeHandle.BottomRight:
                    SelectionCanvas.Cursor = Cursors.SizeNWSE;
                    break;
                case ResizeHandle.TopRight:
                case ResizeHandle.BottomLeft:
                    SelectionCanvas.Cursor = Cursors.SizeNESW;
                    break;
                case ResizeHandle.Top:
                case ResizeHandle.Bottom:
                    SelectionCanvas.Cursor = Cursors.SizeNS;
                    break;
                case ResizeHandle.Left:
                case ResizeHandle.Right:
                    SelectionCanvas.Cursor = Cursors.SizeWE;
                    break;
                default:
                    SelectionCanvas.Cursor = Cursors.Arrow;
                    break;
            }
        }

        private void UpdateHandles()
        {
            if (SelectionRect.Visibility != Visibility.Visible)
            {
                HandleTopLeft.Visibility = Visibility.Collapsed;
                HandleTopRight.Visibility = Visibility.Collapsed;
                HandleBottomLeft.Visibility = Visibility.Collapsed;
                HandleBottomRight.Visibility = Visibility.Collapsed;
                HandleTop.Visibility = Visibility.Collapsed;
                HandleBottom.Visibility = Visibility.Collapsed;
                HandleLeft.Visibility = Visibility.Collapsed;
                HandleRight.Visibility = Visibility.Collapsed;
                return;
            }

            double left = Canvas.GetLeft(SelectionRect);
            double top = Canvas.GetTop(SelectionRect);
            double right = left + SelectionRect.Width;
            double bottom = top + SelectionRect.Height;
            double centerX = left + SelectionRect.Width / 2;
            double centerY = top + SelectionRect.Height / 2;

            Canvas.SetLeft(HandleTopLeft, left - 4);
            Canvas.SetTop(HandleTopLeft, top - 4);
            Canvas.SetLeft(HandleTopRight, right - 4);
            Canvas.SetTop(HandleTopRight, top - 4);
            Canvas.SetLeft(HandleBottomLeft, left - 4);
            Canvas.SetTop(HandleBottomLeft, bottom - 4);
            Canvas.SetLeft(HandleBottomRight, right - 4);
            Canvas.SetTop(HandleBottomRight, bottom - 4);
            Canvas.SetLeft(HandleTop, centerX - 4);
            Canvas.SetTop(HandleTop, top - 4);
            Canvas.SetLeft(HandleBottom, centerX - 4);
            Canvas.SetTop(HandleBottom, bottom - 4);
            Canvas.SetLeft(HandleLeft, left - 4);
            Canvas.SetTop(HandleLeft, centerY - 4);
            Canvas.SetLeft(HandleRight, right - 4);
            Canvas.SetTop(HandleRight, centerY - 4);

            HandleTopLeft.Visibility = Visibility.Visible;
            HandleTopRight.Visibility = Visibility.Visible;
            HandleBottomLeft.Visibility = Visibility.Visible;
            HandleBottomRight.Visibility = Visibility.Visible;
            HandleTop.Visibility = Visibility.Visible;
            HandleBottom.Visibility = Visibility.Visible;
            HandleLeft.Visibility = Visibility.Visible;
            HandleRight.Visibility = Visibility.Visible;
        }

        private void ConstrainSelectionToImage()
        {
            double left = Canvas.GetLeft(SelectionRect);
            double top = Canvas.GetTop(SelectionRect);
            double width = SelectionRect.Width;
            double height = SelectionRect.Height;

            // 确保选区在图像范围内
            left = Math.Max(_imageBounds.Left, Math.Min(left, _imageBounds.Right - width));
            top = Math.Max(_imageBounds.Top, Math.Min(top, _imageBounds.Bottom - height));
            width = Math.Min(width, _imageBounds.Right - left);
            height = Math.Min(height, _imageBounds.Bottom - top);

            Canvas.SetLeft(SelectionRect, left);
            Canvas.SetTop(SelectionRect, top);
            SelectionRect.Width = width;
            SelectionRect.Height = height;
        }

        private void UpdateInfoDisplay(System.Windows.Point mousePos)
        {
            if (SelectionRect.Visibility == Visibility.Visible)
            {
                var rect = GetSelectedRegionInVideoSpace();
                InfoText.Text = $"选区: X={rect.X}, Y={rect.Y}, W={rect.Width}, H={rect.Height}\n" +
                               $"视频: {_videoResolution.Width}x{_videoResolution.Height}";
                InfoBorder.Visibility = Visibility.Visible;

                // 将信息框放在选区上方
                double left = Canvas.GetLeft(SelectionRect);
                double top = Canvas.GetTop(SelectionRect);
                Canvas.SetLeft(InfoBorder, left);
                Canvas.SetTop(InfoBorder, Math.Max(0, top - 50));
            }
            else
            {
                InfoBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            ClearSelection();
        }

        private void ClearSelection()
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            UpdateHandles();
            InfoBorder.Visibility = Visibility.Collapsed;
            Confirm.IsEnabled = false;
            Clear.IsEnabled = false;
            ProcessVideo.IsEnabled = false;
        }

        private void ConfirmRegion_Click(object sender, RoutedEventArgs e)
        {
            // 将 WPF 坐标转换为视频原始分辨率坐标
            var rect = GetSelectedRegionInVideoSpace();
            SelectedRegion = rect;

            // 保存选区信息到JSON文件
            try
            {
                SaveRegionToJson(rect);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存选区信息失败：{ex.Message}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            MessageBox.Show($"已选择区域：{rect.X}, {rect.Y}, {rect.Width}x{rect.Height}\n" +
                            $"视频分辨率：{_videoResolution.Width}x{_videoResolution.Height}\n" +
                            $"时间点：{FormatTimeString(_currentTimeSeconds)}\n" +
                            $"选区信息已保存到JSON文件",
                            "区域已确认", MessageBoxButton.OK, MessageBoxImage.Information);

            // 启用处理视频按钮
            ProcessVideo.IsEnabled = true;
        }

        private string GetJsonFilePath()
        {
            if (string.IsNullOrEmpty(_videoPath))
                return System.IO.Path.Combine(Environment.CurrentDirectory, "region_info.json");

            // 使用视频文件所在目录
            string videoDir = System.IO.Path.GetDirectoryName(_videoPath);
            string videoName = System.IO.Path.GetFileNameWithoutExtension(_videoPath);
            return System.IO.Path.Combine(videoDir, $"{videoName}_region.json");
        }

        private void SaveRegionToJson(System.Drawing.Rectangle rect)
        {
            var regionInfo = new RegionInfo
            {
                VideoPath = _videoPath,
                TimeCode = FormatTimeString(_currentTimeSeconds),
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height,
                VideoWidth = _videoResolution.Width,
                VideoHeight = _videoResolution.Height
            };

            string jsonPath = GetJsonFilePath();
            string json = JsonConvert.SerializeObject(regionInfo, Formatting.Indented);
            File.WriteAllText(jsonPath, json, Encoding.UTF8);
        }

        private void TryAutoLoadRegion()
        {
            if (string.IsNullOrEmpty(_videoPath)) return;

            try
            {
                string jsonPath = GetJsonFilePath();
                if (!File.Exists(jsonPath))
                    return; // 静默失败，不显示错误

                LoadRegionFromFile(jsonPath, showMessage: false);
            }
            catch
            {
                // 静默失败
            }
        }

        private void LoadRegion_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_videoPath))
            {
                MessageBox.Show("请先打开视频文件", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                string jsonPath = GetJsonFilePath();
                if (!File.Exists(jsonPath))
                {
                    MessageBox.Show($"未找到选区信息文件：{jsonPath}", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                LoadRegionFromFile(jsonPath, showMessage: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载选区信息失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadRegionFromFile(string jsonPath, bool showMessage)
        {
            string json = File.ReadAllText(jsonPath, Encoding.UTF8);
            var regionInfo = JsonConvert.DeserializeObject<RegionInfo>(json);

            if (regionInfo == null)
            {
                if (showMessage)
                    MessageBox.Show("JSON文件格式错误", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 验证视频路径是否匹配（允许文件名不同，但建议相同）
            if (!string.IsNullOrEmpty(regionInfo.VideoPath) &&
                System.IO.Path.GetFileName(regionInfo.VideoPath) != System.IO.Path.GetFileName(_videoPath))
            {
                if (showMessage)
                {
                    var result = MessageBox.Show(
                        $"JSON中的视频文件与当前打开的视频不同。\n" +
                        $"JSON: {System.IO.Path.GetFileName(regionInfo.VideoPath)}\n" +
                        $"当前: {System.IO.Path.GetFileName(_videoPath)}\n\n" +
                        $"是否继续加载？",
                        "提示", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                        return;
                }
                else
                {
                    return; // 自动加载时，如果视频不匹配则跳过
                }
            }

            // 跳转到指定时间
            if (!string.IsNullOrEmpty(regionInfo.TimeCode))
            {
                try
                {
                    double timeSeconds = ParseTimeString(regionInfo.TimeCode);
                    LoadFrameAtTime(_videoPath, timeSeconds);
                    TimeInput.Text = regionInfo.TimeCode;
                }
                catch
                {
                    // 如果时间解析失败，继续加载区域
                }
            }

            // 等待图像加载完成后再设置选区
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyRegionFromJson(regionInfo);
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            if (showMessage)
            {
                MessageBox.Show($"已加载选区信息\n" +
                               $"时间：{regionInfo.TimeCode}\n" +
                               $"区域：{regionInfo.X}, {regionInfo.Y}, {regionInfo.Width}x{regionInfo.Height}",
                               "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ApplyRegionFromJson(RegionInfo regionInfo)
        {
            if (regionInfo == null) return;

            // 检查视频分辨率是否匹配
            if (regionInfo.VideoWidth != _videoResolution.Width ||
                regionInfo.VideoHeight != _videoResolution.Height)
            {
                var result = MessageBox.Show(
                    $"JSON中的视频分辨率 ({regionInfo.VideoWidth}x{regionInfo.VideoHeight}) " +
                    $"与当前视频分辨率 ({_videoResolution.Width}x{_videoResolution.Height}) 不匹配。\n\n" +
                    $"是否继续应用选区？（坐标可能会不准确）",
                    "警告", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            // 更新图像边界
            UpdateImageBounds();

            // 计算缩放比例（从视频坐标转换到Canvas坐标）
            double scaleX = _imageBounds.Width / _videoResolution.Width;
            double scaleY = _imageBounds.Height / _videoResolution.Height;

            // 转换坐标
            double canvasX = _imageBounds.Left + regionInfo.X * scaleX;
            double canvasY = _imageBounds.Top + regionInfo.Y * scaleY;
            double canvasW = regionInfo.Width * scaleX;
            double canvasH = regionInfo.Height * scaleY;

            // 设置选区
            Canvas.SetLeft(SelectionRect, canvasX);
            Canvas.SetTop(SelectionRect, canvasY);
            SelectionRect.Width = canvasW;
            SelectionRect.Height = canvasH;
            SelectionRect.Visibility = Visibility.Visible;

            // 更新手柄和状态
            UpdateHandles();
            Confirm.IsEnabled = true;
            Clear.IsEnabled = true;
            ProcessVideo.IsEnabled = true;

            // 更新显示
            SelectedRegion = new System.Drawing.Rectangle(regionInfo.X, regionInfo.Y, regionInfo.Width, regionInfo.Height);
            UpdateInfoDisplay(new System.Windows.Point(canvasX, canvasY));
        }

        private System.Drawing.Rectangle GetSelectedRegionInVideoSpace()
        {
            if (SelectionRect.Visibility != Visibility.Visible)
                return new System.Drawing.Rectangle();

            // 使用已计算的图像边界
            if (_imageBounds.Width <= 0 || _imageBounds.Height <= 0)
            {
                UpdateImageBounds();
            }

            // 计算缩放比例
            double scaleX = _videoResolution.Width / _imageBounds.Width;
            double scaleY = _videoResolution.Height / _imageBounds.Height;

            // 获取选择框相对于图像边界的坐标
            double selX = Canvas.GetLeft(SelectionRect) - _imageBounds.Left;
            double selY = Canvas.GetTop(SelectionRect) - _imageBounds.Top;
            double selW = SelectionRect.Width;
            double selH = SelectionRect.Height;

            // 转换回视频原始坐标
            int x = (int)Math.Round(selX * scaleX);
            int y = (int)Math.Round(selY * scaleY);
            int w = (int)Math.Round(selW * scaleX);
            int h = (int)Math.Round(selH * scaleY);

            // 边界保护
            x = Math.Max(0, Math.Min(x, _videoResolution.Width - 1));
            y = Math.Max(0, Math.Min(y, _videoResolution.Height - 1));
            w = Math.Max(1, Math.Min(w, _videoResolution.Width - x));
            h = Math.Max(1, Math.Min(h, _videoResolution.Height - y));

            return new System.Drawing.Rectangle(x, y, w, h);
        }

        private BitmapSource MatToBitmapSource(Mat mat)
        {
            if (mat == null || mat.Empty())
            {
                return null;
            }

            // 1. 确定像素格式
            PixelFormat pixelFormat;

            // OpenCvSharp 的 Type 通常是:
            // CV_8UC1 (灰度), CV_8UC3 (BGR), CV_8UC4 (BGRA)
            switch (mat.Type().ToString())
            {
                case "CV_8UC1":
                    pixelFormat = PixelFormats.Gray8;
                    break;
                case "CV_8UC3":
                    pixelFormat = PixelFormats.Bgr24; // OpenCV 默认是 BGR
                    break;
                case "CV_8UC4":
                    pixelFormat = PixelFormats.Bgra32; // 带 Alpha 通道
                    break;
                default:
                    throw new ArgumentException($"不支持的 Mat 类型: {mat.Type()}");
            }

            // 2. 计算图像数据大小
            // stride (步幅) = 每一行占用的字节数 (包含填充)
            int stride = (int)mat.Step();
            int size = (int)mat.Total() * mat.ElemSize();

            // 3. 创建 BitmapSource
            // 使用 Create 方法直接从内存指针创建，避免了转成 System.Drawing.Bitmap 的中间损耗
            BitmapSource bitmapSource = BitmapSource.Create(
                mat.Width,
                mat.Height,
                96d, 96d, // DPI 设置，通常设为 96
                pixelFormat,
                null, // 调色板
                mat.Data, // 直接使用 Mat 的数据指针
                size,
                stride
            );

            // 4. 冻结对象 (重要)
            // 这样做可以让 BitmapSource 在非 UI 线程创建后，被 UI 线程访问
            bitmapSource.Freeze();

            return bitmapSource;
        }

        private void ProcessVideo_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_videoPath))
            {
                MessageBox.Show("请先打开视频文件", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (SelectedRegion.Width <= 0 || SelectedRegion.Height <= 0)
            {
                MessageBox.Show("请先选择有效的区域", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 获取处理范围
            bool limitToFirstMinute = ProcessFirstMinute.IsChecked == true;

            // 生成字幕文件名（与视频文件名一致）
            string videoDir = System.IO.Path.GetDirectoryName(_videoPath);
            string videoName = System.IO.Path.GetFileNameWithoutExtension(_videoPath);
            string srtPath = System.IO.Path.Combine(videoDir, $"{videoName}.srt");

            // 隐藏窗口
            this.Hide();

            // 在后台线程运行（避免阻塞 UI）
            Task.Run(() =>
            {
                try
                {
                    var generator = new VideoProcessor(
                        _videoPath,
                        SelectedRegion,
                        intervalSeconds: 0.5,
                        limitToFirstMinute: limitToFirstMinute
                    );

                    generator.GenerateSrt(engine, srtPath);

                    // 处理完成，恢复界面
                    Dispatcher.Invoke(() =>
                    {
                        this.Show();
                        this.WindowState = WindowState.Normal;
                        this.Activate();
                        MessageBox.Show($"字幕生成完成！\n保存位置：{srtPath}",
                            "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                catch (Exception ex)
                {
                    // 处理失败，恢复界面
                    Dispatcher.Invoke(() =>
                    {
                        this.Show();
                        this.WindowState = WindowState.Normal;
                        this.Activate();
                        MessageBox.Show($"处理失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            });
        }
    }
}
