using OpenCvSharp;
using PaddleOCRSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        private double _totalDurationSeconds = 0; // 视频总时长（秒）
        private double _videoFps = 0; // 视频帧率
        private bool _isSliderDragging = false; // 滑块是否正在拖动
        private bool _keepSelectionVisible = false; // 是否保持选区可见
        private bool _isEditingSubtitle = false; // 标记是否正在编辑字幕
        PaddleOCREngine engine;

        // 存储用户选择的区域（GDI Rectangle）
        public System.Drawing.Rectangle SelectedRegion { get; private set; }

        // 字幕列表
        public ObservableCollection<SubtitleItem> Subtitles { get; set; } = new ObservableCollection<SubtitleItem>();

        // 当前处理的字幕列表（用于导出）
        private List<SrtEntry> _currentSrtEntries = new List<SrtEntry>();

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

            // 监听容器大小变化（在InitializeComponent之后）
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var container = this.FindName("PreviewContainer") as FrameworkElement;
                if (container != null)
                {
                    container.SizeChanged += (s, e) => UpdateImageBounds();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            // 监听窗口大小变化
            this.SizeChanged += (s, e) =>
            {
                // 延迟更新，等待布局完成
                Dispatcher.BeginInvoke(new Action(() => UpdateImageBounds()),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            };

            // 绑定字幕列表
            SubtitleListBox.ItemsSource = Subtitles;

            // 初始化进度面板
            ProgressPanel.Visibility = Visibility.Collapsed;
            // 初始化状态文本（始终可见）
            ProgressStatusText.Text = "";
            ProgressSpeedText.Text = "";
        }

        private void UpdateImageBounds()
        {
            if (PreviewImage.Source == null) return;

            var source = PreviewImage.Source as BitmapSource;
            if (source == null) return;

            // 使用PreviewContainer的实际大小来计算图像显示区域
            // Canvas覆盖整个Grid，所以坐标是相对于Grid的
            FrameworkElement container = null;
            try
            {
                container = this.FindName("PreviewContainer") as FrameworkElement;
            }
            catch { }

            if (container == null || container.ActualWidth <= 0 || container.ActualHeight <= 0)
            {
                // 如果容器还没渲染，使用Image控件的大小
                if (PreviewImage.ActualWidth <= 0 || PreviewImage.ActualHeight <= 0)
                    return;
                container = PreviewImage;
            }

            // 计算图像在容器中的实际显示区域（考虑Stretch="Uniform"）
            double scale = Math.Min(
                container.ActualWidth / source.PixelWidth,
                container.ActualHeight / source.PixelHeight);

            double renderedWidth = source.PixelWidth * scale;
            double renderedHeight = source.PixelHeight * scale;

            // Canvas覆盖整个Grid，所以偏移是相对于Grid的
            // 图像在Grid中居中显示
            double offsetX = (container.ActualWidth - renderedWidth) / 2;
            double offsetY = (container.ActualHeight - renderedHeight) / 2;

            _imageBounds = new System.Windows.Rect(
                offsetX,
                offsetY,
                renderedWidth,
                renderedHeight);

            // 如果选区存在，更新选区位置以适应新的边界
            if (SelectionRect.Visibility == Visibility.Visible)
            {
                ConstrainSelectionToImage();
                UpdateHandles();
            }
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
                _totalDurationSeconds = totalDuration;
                timeSeconds = Math.Max(0, Math.Min(timeSeconds, totalDuration));
                _currentTimeSeconds = timeSeconds;

                // 更新滑块范围
                TimeSlider.Maximum = 1000;
                TimeSlider.IsEnabled = true;
                _isSliderDragging = false; // 重置拖动状态
                UpdateTimeSliderPosition();
                UpdateTimeDisplay();

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

                // 清除之前的选区（如果未设置保持可见）
                if (!_keepSelectionVisible)
                {
                    ClearSelection();
                }

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
                CurrentTimeText.Text = $"{timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            }
            else
            {
                CurrentTimeText.Text = $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            }
        }

        private void UpdateTimeDisplay()
        {
            UpdateCurrentTimeDisplay();

            // 更新总时长显示
            if (_totalDurationSeconds > 0)
            {
                var totalSpan = TimeSpan.FromSeconds(_totalDurationSeconds);
                if (totalSpan.TotalHours >= 1)
                {
                    TotalTimeText.Text = $"{totalSpan.Hours:D2}:{totalSpan.Minutes:D2}:{totalSpan.Seconds:D2}";
                }
                else
                {
                    TotalTimeText.Text = $"{totalSpan.Minutes:D2}:{totalSpan.Seconds:D2}";
                }
            }
        }

        private void UpdateTimeSliderPosition()
        {
            if (_totalDurationSeconds > 0 && !_isSliderDragging)
            {
                double ratio = _currentTimeSeconds / _totalDurationSeconds;
                TimeSlider.Value = ratio * 1000;
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

        private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isSliderDragging && _totalDurationSeconds > 0 && !string.IsNullOrEmpty(_videoPath))
            {
                double ratio = TimeSlider.Value / 1000.0;
                double targetTime = ratio * _totalDurationSeconds;
                LoadFrameAtTime(_videoPath, targetTime);
            }
        }

        private void TimeSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isSliderDragging = true;
        }

        private void TimeSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSliderDragging && _totalDurationSeconds > 0 && !string.IsNullOrEmpty(_videoPath))
            {
                double ratio = TimeSlider.Value / 1000.0;
                double targetTime = ratio * _totalDurationSeconds;
                LoadFrameAtTime(_videoPath, targetTime);
            }
            _isSliderDragging = false;
        }

        // 拖拽支持
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    string filePath = files[0];
                    // 检查是否是视频文件
                    string ext = System.IO.Path.GetExtension(filePath).ToLower();
                    if (ext == ".mp4" || ext == ".avi" || ext == ".mov" || ext == ".mkv")
                    {
                        _videoPath = filePath;
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
            // 限制在图像区域内 - 允许到达边界（使用<=而不是<）
            current.X = Math.Max(_imageBounds.Left, Math.Min(_imageBounds.Right, current.X));
            current.Y = Math.Max(_imageBounds.Top, Math.Min(_imageBounds.Bottom, current.Y));

            double x = Math.Min(_startPoint.X, current.X);
            double y = Math.Min(_startPoint.Y, current.Y);
            double width = Math.Abs(current.X - _startPoint.X);
            double height = Math.Abs(current.Y - _startPoint.Y);

            // 确保选区不超出图像边界 - 允许到达边界
            // 使用<=确保可以到达右边界
            if (x + width > _imageBounds.Right)
            {
                width = _imageBounds.Right - x;
            }
            if (y + height > _imageBounds.Bottom)
            {
                height = _imageBounds.Bottom - y;
            }

            // 确保最小尺寸
            if (width < 10) width = 10;
            if (height < 10) height = 10;

            // 确保不超出左边界
            if (x < _imageBounds.Left)
            {
                width = width - (_imageBounds.Left - x);
                x = _imageBounds.Left;
                if (width < 10) width = 10;
            }
            if (y < _imageBounds.Top)
            {
                height = height - (_imageBounds.Top - y);
                y = _imageBounds.Top;
                if (height < 10) height = 10;
            }

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

            // 限制在图像区域内 - 修复右侧边界问题
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

            // 限制在图像区域内 - 允许到达边界
            // 先限制right和bottom，允许它们等于边界
            right = Math.Min(right, _imageBounds.Right);
            bottom = Math.Min(bottom, _imageBounds.Bottom);

            // 然后限制left和top，确保选区不超出左边界和上边界
            left = Math.Max(_imageBounds.Left, left);
            top = Math.Max(_imageBounds.Top, top);

            // 如果调整后导致尺寸太小，调整另一边
            if (right - left < 10)
            {
                if (_resizeHandle == ResizeHandle.Left || _resizeHandle == ResizeHandle.TopLeft || _resizeHandle == ResizeHandle.BottomLeft)
                    left = right - 10;
                else
                    right = left + 10;
            }
            if (bottom - top < 10)
            {
                if (_resizeHandle == ResizeHandle.Top || _resizeHandle == ResizeHandle.TopLeft || _resizeHandle == ResizeHandle.TopRight)
                    top = bottom - 10;
                else
                    bottom = top + 10;
            }

            // 最终确保不超出边界
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

            // 确保选区在图像范围内 - 允许到达边界
            left = Math.Max(_imageBounds.Left, Math.Min(left, _imageBounds.Right - width));
            top = Math.Max(_imageBounds.Top, Math.Min(top, _imageBounds.Bottom - height));
            // 允许width到达右边界
            width = Math.Min(width, _imageBounds.Right - left);
            height = Math.Min(height, _imageBounds.Bottom - top);

            // 确保最小尺寸
            if (width < 10) width = 10;
            if (height < 10) height = 10;

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
            // 如果设置了保持可见，则不隐藏
            if (_keepSelectionVisible && SelectionRect.Visibility == Visibility.Visible)
            {
                return;
            }
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

            // 获取设置参数
            int detectionFps = 5;
            int minDurationMs = 200;
            if (int.TryParse(DetectionFpsInput.Text, out int fps))
            {
                detectionFps = Math.Max(1, Math.Min(999, fps));
            }
            if (int.TryParse(MinDurationMsInput.Text, out int minMs))
            {
                minDurationMs = Math.Max(1, Math.Min(5000, minMs));
            }

            // 获取处理范围
            bool limitToFirstMinute = ProcessFirstMinute.IsChecked == true;

            // 生成字幕文件名（与视频文件名一致）
            string videoDir = System.IO.Path.GetDirectoryName(_videoPath);
            string videoName = System.IO.Path.GetFileNameWithoutExtension(_videoPath);
            string srtPath = System.IO.Path.Combine(videoDir, $"{videoName}.srt");

            // 清空之前的字幕列表
            Subtitles.Clear();
            _currentSrtEntries.Clear();

            // 禁用处理按钮，显示进度面板
            ProcessVideo.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressBar.Value = 0;
            ProgressStatusText.Text = "处理中...";
            ExportSrtButton.Visibility = Visibility.Collapsed;

            // 在后台线程运行（避免阻塞 UI）
            Task.Run(() =>
            {
                try
                {
                    var generator = new VideoProcessor(
                        _videoPath,
                        SelectedRegion,
                        detectionFps: detectionFps,
                        minDurationMs: minDurationMs,
                        limitToFirstMinute: limitToFirstMinute
                    );

                    var progress = new Progress<ProgressInfo>(info =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            // 更新进度
                            if (info.TotalTime > 0)
                            {
                                double progressPercent = (info.CurrentTime / info.TotalTime) * 100;
                                ProgressBar.Value = Math.Min(100, Math.Max(0, progressPercent));
                            }

                            // 更新速度显示
                            ProgressSpeedText.Text = $"[x{info.SpeedRatio:F1}]";
                            if (info.SpeedRatio > 1)
                                ProgressSpeedText.Foreground = new SolidColorBrush(Colors.Green);
                            else
                                ProgressSpeedText.Foreground = new SolidColorBrush(Colors.Red);

                            // 更新时间显示
                            var currentSpan = TimeSpan.FromSeconds(info.CurrentTime);
                            var totalSpan = TimeSpan.FromSeconds(info.TotalTime);
                            ProgressCurrentTimeText.Text = FormatTimeSpan(currentSpan);
                            ProgressTotalTimeText.Text = FormatTimeSpan(totalSpan);

                            // 添加新字幕到列表（使用AddOrMergeSubtitle进行过滤和合并）
                            if (info.LatestSubtitle != null)
                            {
                                // 使用AddOrMergeSubtitle方法进行过滤和合并
                                int entriesCountBefore = _currentSrtEntries.Count;
                                var mergedEntry = AddOrMergeSubtitle(_currentSrtEntries, 
                                    info.LatestSubtitle.Text, 
                                    info.LatestSubtitle.StartTime.TotalSeconds, 
                                    info.LatestSubtitle.EndTime.TotalSeconds);

                                // 检查是否是合并到已有条目还是新条目
                                // 如果entries数量没有增加，说明是合并到已有条目
                                bool isNewEntry = (_currentSrtEntries.Count > entriesCountBefore);
                                
                                if (!isNewEntry)
                                {
                                    // 合并到已有条目，更新UI
                                    int entryIndex = _currentSrtEntries.IndexOf(mergedEntry);
                                    if (entryIndex >= 0 && entryIndex < Subtitles.Count)
                                    {
                                        var existingItem = Subtitles[entryIndex];
                                        existingItem.TimeRange = $"{FormatTimeSpan(mergedEntry.StartTime)} --> {FormatTimeSpan(mergedEntry.EndTime)}";
                                        existingItem.EndTimeSeconds = mergedEntry.EndTime.TotalSeconds;
                                        
                                        // 更新文本（如果文本有变化）
                                        var newLines = mergedEntry.Text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                                        if (newLines.Count == 0) newLines.Add(mergedEntry.Text); // 如果没有换行，保持原样
                                        
                                        if (newLines.Count != existingItem.Lines.Count || 
                                            !newLines.SequenceEqual(existingItem.Lines))
                                        {
                                            existingItem.Lines.Clear();
                                            existingItem.Lines.AddRange(newLines);
                                        }
                                        
                                        // 刷新显示
                                        var collectionView = System.Windows.Data.CollectionViewSource.GetDefaultView(Subtitles);
                                        collectionView.Refresh();
                                    }
                                }
                                else
                                {
                                    // 新条目，添加到UI
                                    var newLines = mergedEntry.Text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                                    if (newLines.Count == 0) newLines.Add(mergedEntry.Text); // 如果没有换行，保持原样
                                    
                                    var subtitleItem = new SubtitleItem
                                    {
                                        TimeRange = $"{FormatTimeSpan(mergedEntry.StartTime)} --> {FormatTimeSpan(mergedEntry.EndTime)}",
                                        Lines = newLines,
                                        StartTimeSeconds = mergedEntry.StartTime.TotalSeconds,
                                        EndTimeSeconds = mergedEntry.EndTime.TotalSeconds
                                    };
                                    Subtitles.Add(subtitleItem);
                                }

                                // 自动滚动到底部
                                if (SubtitleListBox.Items.Count > 0)
                                {
                                    SubtitleListBox.ScrollIntoView(SubtitleListBox.Items[SubtitleListBox.Items.Count - 1]);
                                }
                            }

                            // 处理完成
                            if (info.IsFinished)
                            {
                                ProgressStatusText.Text = "处理完成";
                                ProgressBar.Value = 100;
                                ProgressSpeedText.Foreground = new SolidColorBrush(Colors.Green);
                                ExportSrtButton.Visibility = Visibility.Visible;
                                ProcessVideo.IsEnabled = true;
                            }
                        });
                    });

                    generator.GenerateSrt(engine, srtPath, progress);

                    // 处理完成提示
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"字幕生成完成！\n保存位置：{srtPath}\n字幕条数：{Subtitles.Count}",
                            "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                catch (Exception ex)
                {
                    // 处理失败
                    Dispatcher.Invoke(() =>
                    {
                        ProgressStatusText.Text = "处理失败";
                        ProgressSpeedText.Text = "";
                        ProcessVideo.IsEnabled = true;
                        MessageBox.Show($"处理失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            });
        }

        private string FormatTimeSpan(TimeSpan span)
        {
            if (span.TotalHours >= 1)
            {
                return $"{span.Hours:D2}:{span.Minutes:D2}:{span.Seconds:D2}";
            }
            else
            {
                return $"{span.Minutes:D2}:{span.Seconds:D2}";
            }
        }

        private void SubtitleListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 检查点击的是否是TextBox
            var source = e.OriginalSource as System.Windows.DependencyObject;
            while (source != null)
            {
                if (source is TextBox)
                {
                    // 点击的是TextBox，不处理选择事件，让TextBox获得焦点
                    _isEditingSubtitle = true;
                    return;
                }
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            }
            _isEditingSubtitle = false;
        }

        private void SubtitleListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 避免在编辑时触发跳转
            if (e.AddedItems.Count == 0 || _isEditingSubtitle) return;
            
            if (SubtitleListBox.SelectedItem is SubtitleItem item && !string.IsNullOrEmpty(_videoPath))
            {
                // 跳转到字幕结束时间（因为开始时间可能字幕还没完全展示）
                LoadFrameAtTime(_videoPath, item.EndTimeSeconds);
            }
        }

        private void SubtitleTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            // 获取原始文本（DataContext）
            var originalText = textBox.DataContext as string;
            if (originalText == null) return;

            // 向上查找ListBoxItem以获取SubtitleItem
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(textBox);
            while (parent != null && !(parent is ListBoxItem))
            {
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }

            if (parent is ListBoxItem listBoxItem)
            {
                var subtitleItem = listBoxItem.DataContext as SubtitleItem;
                if (subtitleItem != null)
                {
                    // 更新字幕文本
                    var newText = textBox.Text;
                    var lineIndex = subtitleItem.Lines.IndexOf(originalText);

                    if (lineIndex >= 0)
                    {
                        subtitleItem.Lines[lineIndex] = newText;

                        // 同步更新_currentSrtEntries
                        var subtitleIndex = Subtitles.IndexOf(subtitleItem);
                        if (subtitleIndex >= 0 && subtitleIndex < _currentSrtEntries.Count)
                        {
                            _currentSrtEntries[subtitleIndex].Text = string.Join("\n", subtitleItem.Lines);
                        }
                    }
                }
            }
            
            // 编辑完成，重置标志
            _isEditingSubtitle = false;
        }

        private void ToggleSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            _keepSelectionVisible = !_keepSelectionVisible;

            if (_keepSelectionVisible)
            {
                // 显示选区
                if (SelectedRegion.Width > 0 && SelectedRegion.Height > 0)
                {
                    // 如果已有选区，显示它
                    UpdateImageBounds();
                    var scaleX = _imageBounds.Width / _videoResolution.Width;
                    var scaleY = _imageBounds.Height / _videoResolution.Height;
                    var canvasX = _imageBounds.Left + SelectedRegion.X * scaleX;
                    var canvasY = _imageBounds.Top + SelectedRegion.Y * scaleY;
                    var canvasW = SelectedRegion.Width * scaleX;
                    var canvasH = SelectedRegion.Height * scaleY;

                    Canvas.SetLeft(SelectionRect, canvasX);
                    Canvas.SetTop(SelectionRect, canvasY);
                    SelectionRect.Width = canvasW;
                    SelectionRect.Height = canvasH;
                    SelectionRect.Visibility = Visibility.Visible;
                    UpdateHandles();
                }
                else if (SelectionRect.Visibility == Visibility.Collapsed)
                {
                    // 如果没有选区，提示用户
                    MessageBox.Show("请先选择区域", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    _keepSelectionVisible = false;
                    ToggleSelectionButton.Content = "显示选区";
                    return;
                }
                ToggleSelectionButton.Content = "隐藏选区";
            }
            else
            {
                // 隐藏选区
                SelectionRect.Visibility = Visibility.Collapsed;
                UpdateHandles();
                InfoBorder.Visibility = Visibility.Collapsed;
                ToggleSelectionButton.Content = "显示选区";
            }
        }

        private void ExportSrtButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSrtEntries.Count == 0)
            {
                MessageBox.Show("没有可导出的字幕", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "字幕文件|*.srt|所有文件|*.*",
                FileName = System.IO.Path.GetFileNameWithoutExtension(_videoPath) + ".srt"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    WriteSrtFile(dialog.FileName, _currentSrtEntries);
                    MessageBox.Show($"字幕已导出到：{dialog.FileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void WriteSrtFile(string path, List<SrtEntry> entries)
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                writer.WriteLine(i + 1);
                writer.WriteLine($"{entry.StartTime:hh\\:mm\\:ss\\,fff} --> {entry.EndTime:hh\\:mm\\:ss\\,fff}");
                writer.WriteLine(entry.Text);
                writer.WriteLine();
            }
        }

        private void StartOcrProcessing()
        {
            // 保留此方法以兼容旧代码，但实际使用ProcessVideo_Click
            ProcessVideo_Click(null, null);
        }

        // 从VideoProcessor复制的字幕合并逻辑
        private SrtEntry AddOrMergeSubtitle(List<SrtEntry> entries, string text, double start, double end)
        {
            if (entries.Count > 0)
            {
                var last = entries[entries.Count - 1];

                // 1. 文本完全相同，合并
                if (last.Text == text)
                {
                    last.EndTime = TimeSpan.FromSeconds(end);
                    return last;
                }

                // 2. 文本相似度高，且时间重叠或紧邻，合并
                // 计算时间间隔
                double gap = start - last.EndTime.TotalSeconds;
                int lastLength = last.Text.Length;
                int currentLength = text.Length;
                if (gap < 0.5 && CalculateLevenshteinSimilarity(last.Text, text.Substring(0, Math.Min(lastLength, currentLength))) > 0.8)
                {
                    // 相似合并，取较长的一个
                    if (currentLength > lastLength) last.Text = text;
                    last.EndTime = TimeSpan.FromSeconds(end);
                    return last;
                }
            }

            var newEntry = new SrtEntry
            {
                Index = entries.Count + 1,
                StartTime = TimeSpan.FromSeconds(start),
                EndTime = TimeSpan.FromSeconds(end),
                Text = text
            };
            entries.Add(newEntry);
            return newEntry;
        }

        private double CalculateLevenshteinSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0;
            int len1 = s1.Length;
            int len2 = s2.Length;
            var d = new int[len1 + 1, len2 + 1];
            for (int i = 0; i <= len1; i++) d[i, 0] = i;
            for (int j = 0; j <= len2; j++) d[0, j] = j;
            for (int i = 1; i <= len1; i++)
            {
                for (int j = 1; j <= len2; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return 1.0 - (double)d[len1, len2] / Math.Max(len1, len2);
        }
    }
}
