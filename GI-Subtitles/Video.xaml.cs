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
    /// Video.xaml interaction logic
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
        private System.Windows.Rect _imageBounds; // The actual display area of the image in the Canvas
        private double _currentTimeSeconds = 0; // The current display time point (seconds)
        private double _totalDurationSeconds = 0; // The total duration of the video (seconds)
        private double _videoFps = 0; // The frame rate of the video
        private bool _isSliderDragging = false; // Whether the slider is being dragged
        private bool _keepSelectionVisible = false; // Whether to keep the selection visible
        private bool _isEditingSubtitle = false; // Whether to mark whether the subtitle is being edited
        private VideoCapture videoCapture;
        PaddleOCREngine engine;

        // Store the user-selected region (GDI Rectangle)
        public System.Drawing.Rectangle SelectedRegion { get; private set; }

        // Subtitle list
        public ObservableCollection<SubtitleItem> Subtitles { get; set; } = new ObservableCollection<SubtitleItem>();

        // The current list of subtitles being processed (for export)
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

            // Calculate the image boundaries
            PreviewImage.Loaded += (s, e) => UpdateImageBounds();
            PreviewImage.SizeChanged += (s, e) => UpdateImageBounds();

            // Listen for container size changes (after InitializeComponent)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var container = this.FindName("PreviewContainer") as FrameworkElement;
                if (container != null)
                {
                    container.SizeChanged += (s, e) => UpdateImageBounds();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            // Listen for window size changes
            this.SizeChanged += (s, e) =>
            {
                // Delay update, wait for layout to complete
                Dispatcher.BeginInvoke(new Action(() => UpdateImageBounds()),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            };

            // Bind the subtitle list
            SubtitleListBox.ItemsSource = Subtitles;

            // Initialize the progress panel
            ProgressPanel.Visibility = Visibility.Collapsed;
            // Initialize the status text (always visible)
            ProgressStatusText.Text = "";
            ProgressSpeedText.Text = "";
        }

        private void UpdateImageBounds()
        {
            if (PreviewImage.Source == null) return;

            var source = PreviewImage.Source as BitmapSource;
            if (source == null) return;

            // Use the actual size of PreviewContainer to calculate the image display area
            // The canvas covers the entire grid, so the coordinates are relative to the grid
            FrameworkElement container = null;
            try
            {
                container = this.FindName("PreviewContainer") as FrameworkElement;
            }
            catch { }

            if (container == null || container.ActualWidth <= 0 || container.ActualHeight <= 0)
            {
                // If the container hasn't been rendered, use the size of the Image control
                if (PreviewImage.ActualWidth <= 0 || PreviewImage.ActualHeight <= 0)
                    return;
                container = PreviewImage;
            }

            // Calculate the actual display area of the image in the container (considering Stretch="Uniform")
            double scale = Math.Min(
                container.ActualWidth / source.PixelWidth,
                container.ActualHeight / source.PixelHeight);

            double renderedWidth = source.PixelWidth * scale;
            double renderedHeight = source.PixelHeight * scale;

            // The canvas covers the entire grid, so the offset is relative to the grid
            // The image is centered in the grid
            double offsetX = (container.ActualWidth - renderedWidth) / 2;
            double offsetY = (container.ActualHeight - renderedHeight) / 2;

            _imageBounds = new System.Windows.Rect(
                offsetX,
                offsetY,
                renderedWidth,
                renderedHeight);

            // If the selection exists, update the selection position to fit the new boundaries
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
                Filter = "Video files|*.mp4;*.avi;*.mov;*.mkv|All files|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                _videoPath = dialog.FileName;
                videoCapture = new VideoCapture(_videoPath);
                if (!videoCapture.IsOpened())
                    throw new InvalidOperationException("Failed to open video, please try to convert to .avi format.");

                _videoResolution = new System.Drawing.Size(
                    (int)videoCapture.FrameWidth,
                    (int)videoCapture.FrameHeight);

                _videoFps = videoCapture.Fps;
                if (_videoFps <= 0) _videoFps = 30; // Default frame rate
                LoadFrameAtTime(_videoPath, 0);
                JumpToTime.IsEnabled = true;
                LoadRegion.IsEnabled = true;

                // Automatically try to load the saved selection information
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
                // Jump to the specified time
                double totalDuration = videoCapture.Get(VideoCaptureProperties.FrameCount) / _videoFps;
                _totalDurationSeconds = totalDuration;
                timeSeconds = Math.Max(0, Math.Min(timeSeconds, totalDuration));
                _currentTimeSeconds = timeSeconds;

                // Update the slider range
                TimeSlider.Maximum = 1000;
                TimeSlider.IsEnabled = true;
                _isSliderDragging = false; // Reset the dragging state
                UpdateTimeSliderPosition();
                UpdateTimeDisplay();

                // Jump using milliseconds (more accurate)
                videoCapture.Set(VideoCaptureProperties.PosMsec, timeSeconds * 1000);

                using var mat = new Mat();
                videoCapture.Read(mat); // Read the current frame

                if (mat.Empty())
                    throw new Exception("Failed to read video frame.");

                // Convert to BitmapSource for WPF display
                var bitmapSource = MatToBitmapSource(mat);
                PreviewImage.Source = (BitmapSource)bitmapSource;
                SelectionCanvas.Visibility = Visibility.Visible;

                // Update the current time display
                UpdateCurrentTimeDisplay();

                // Clear the previous selection (if not set to keep visible)
                if (!_keepSelectionVisible)
                {
                    ClearSelection();
                }

                // Update the image boundaries (wait for layout to complete)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateImageBounds();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

            // Update the total duration display
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

            // Supported formats: MM:SS or HH:MM:SS
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

            throw new FormatException("Invalid time format, please use MM:SS or HH:MM:SS format");
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
                MessageBox.Show("Please open the video file first", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                double timeSeconds = ParseTimeString(TimeInput.Text);
                LoadFrameAtTime(_videoPath, timeSeconds);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to jump: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        // Drag support
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
                    // Check if it is a video file
                    string ext = System.IO.Path.GetExtension(filePath).ToLower();
                    if (ext == ".mp4" || ext == ".avi" || ext == ".mov" || ext == ".mkv")
                    {
                        _videoPath = filePath;
                        LoadFrameAtTime(_videoPath, 0);
                        JumpToTime.IsEnabled = true;
                        LoadRegion.IsEnabled = true;

                        // Automatically try to load the saved selection information
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

            // Check if it is clicked on the adjustment handle
            _resizeHandle = GetResizeHandle(pos);
            if (_resizeHandle != ResizeHandle.None)
            {
                _isResizing = true;
                _lastMousePos = pos;
                e.Handled = true;
                return;
            }

            // Check if it is clicked on the selection area
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

            // Start creating a new selection area
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

            // Update the coordinate information display
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
                // Update the mouse cursor
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

                // Verify that the selection is valid
                if (SelectionRect.Width < 5 || SelectionRect.Height < 5)
                {
                    ClearSelection();
                }
                else
                {
                    // Ensure that the selection is within the image boundaries
                    ConstrainSelectionToImage();
                    UpdateHandles();
                    Confirm.IsEnabled = true;
                    Clear.IsEnabled = true;
                }
            }
        }

        private void CreateSelection(System.Windows.Point current)
        {
            // Limit the selection to the image boundaries - allow reaching the boundaries (using <= instead of <)
            current.X = Math.Max(_imageBounds.Left, Math.Min(_imageBounds.Right, current.X));
            current.Y = Math.Max(_imageBounds.Top, Math.Min(_imageBounds.Bottom, current.Y));

            double x = Math.Min(_startPoint.X, current.X);
            double y = Math.Min(_startPoint.Y, current.Y);
            double width = Math.Abs(current.X - _startPoint.X);
            double height = Math.Abs(current.Y - _startPoint.Y);

            // Ensure that the selection does not exceed the image boundaries - allow reaching the boundaries
            // Use <= to ensure that the right boundary can be reached
            if (x + width > _imageBounds.Right)
            {
                width = _imageBounds.Right - x;
            }
            if (y + height > _imageBounds.Bottom)
            {
                height = _imageBounds.Bottom - y;
            }

            // Ensure the minimum size
            if (width < 10) width = 10;
            if (height < 10) height = 10;

            // Ensure that the selection does not exceed the left boundary
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

            // Limit the selection to the image boundaries - fix the right boundary problem
            newLeft = Math.Max(_imageBounds.Left, Math.Min(newLeft, _imageBounds.Right - SelectionRect.Width));
            newTop = Math.Max(_imageBounds.Top, Math.Min(newTop, _imageBounds.Bottom - SelectionRect.Height));

            Canvas.SetLeft(SelectionRect, newLeft);
            Canvas.SetTop(SelectionRect, newTop);

            _lastMousePos = current;
            UpdateHandles();
        }

        private void ResizeSelection(System.Windows.Point current)
        {
            // Limit the selection to the image boundaries
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

            // Ensure the minimum size
            if (right - left < 10) right = left + 10;
            if (bottom - top < 10) bottom = top + 10;

            // Limit the selection to the image boundaries - allow reaching the boundaries
            // First limit right and bottom, allow them to equal the boundaries
            right = Math.Min(right, _imageBounds.Right);
            bottom = Math.Min(bottom, _imageBounds.Bottom);

            // Then limit left and top, ensure that the selection does not exceed the left and upper boundaries
            left = Math.Max(_imageBounds.Left, left);
            top = Math.Max(_imageBounds.Top, top);

            // If the adjustment causes the size to be too small, adjust the other side
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

            // Finally ensure that the selection does not exceed the boundaries
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

            const double handleSize = 12; // Handle detection area

            // Check each handle
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

            // Ensure that the selection is within the image boundaries - allow reaching the boundaries
            left = Math.Max(_imageBounds.Left, Math.Min(left, _imageBounds.Right - width));
            top = Math.Max(_imageBounds.Top, Math.Min(top, _imageBounds.Bottom - height));
            // Allow width to reach the right boundary
            width = Math.Min(width, _imageBounds.Right - left);
            height = Math.Min(height, _imageBounds.Bottom - top);

            // Ensure the minimum size
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
                InfoText.Text = $"Selection: X={rect.X}, Y={rect.Y}, W={rect.Width}, H={rect.Height}\n" +
                               $"Video: {_videoResolution.Width}x{_videoResolution.Height}";
                InfoBorder.Visibility = Visibility.Visible;

                // Place the information box above the selection
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
            // If the keep selection visible is set, do not hide
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
            // Convert the WPF coordinates to the original video resolution coordinates
            var rect = GetSelectedRegionInVideoSpace();
            SelectedRegion = rect;

            // Save the selection information to the JSON file
            try
            {
                SaveRegionToJson(rect);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save selection information: {ex.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            MessageBox.Show($"Selected region: {rect.X}, {rect.Y}, {rect.Width}x{rect.Height}\n" +
                            $"Video resolution: {_videoResolution.Width}x{_videoResolution.Height}\n" +
                            $"Time: {FormatTimeString(_currentTimeSeconds)}\n" +
                            $"Selection information saved to JSON file",
                            "Region confirmed", MessageBoxButton.OK, MessageBoxImage.Information);

            // Enable the process video button
            ProcessVideo.IsEnabled = true;
        }

        private string GetJsonFilePath()
        {
            if (string.IsNullOrEmpty(_videoPath))
                return System.IO.Path.Combine(Environment.CurrentDirectory, "region_info.json");

            // Use the directory of the video file
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
                    return; // Silent failure, do not show error

                LoadRegionFromFile(jsonPath, showMessage: false);
            }
            catch
            {
                // Silent failure
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
                    MessageBox.Show($"Selection information file not found: {jsonPath}", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                LoadRegionFromFile(jsonPath, showMessage: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load selection information: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadRegionFromFile(string jsonPath, bool showMessage)
        {
            string json = File.ReadAllText(jsonPath, Encoding.UTF8);
            var regionInfo = JsonConvert.DeserializeObject<RegionInfo>(json);

            if (regionInfo == null)
            {
                if (showMessage)
                    MessageBox.Show("JSON file format error", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 验证视频路径是否匹配（允许文件名不同，但建议相同）
            if (!string.IsNullOrEmpty(regionInfo.VideoPath) &&
                System.IO.Path.GetFileName(regionInfo.VideoPath) != System.IO.Path.GetFileName(_videoPath))
            {
                if (showMessage)
                {
                    var result = MessageBox.Show(
                        $"The video file in the JSON is different from the currently opened video.\n" +
                        $"JSON: {System.IO.Path.GetFileName(regionInfo.VideoPath)}\n" +
                        $"Current: {System.IO.Path.GetFileName(_videoPath)}\n\n" +
                        $"Continue loading?",
                        "Question", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                        return;
                }
                else
                {
                    return; // If the video does not match during automatic loading, skip
                }
            }

            // Jump to the specified time
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
                    // If the time parsing fails, continue to load the region
                }
            }

            // Wait for the image to load before setting the selection
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyRegionFromJson(regionInfo);
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            if (showMessage)
            {
                MessageBox.Show($"Selection information loaded\n" +
                               $"Time: {regionInfo.TimeCode}\n" +
                               $"Region: {regionInfo.X}, {regionInfo.Y}, {regionInfo.Width}x{regionInfo.Height}",
                               "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ApplyRegionFromJson(RegionInfo regionInfo)
        {
            if (regionInfo == null) return;

            // Check if the video resolution matches
            if (regionInfo.VideoWidth != _videoResolution.Width ||
                regionInfo.VideoHeight != _videoResolution.Height)
            {
                var result = MessageBox.Show(
                    $"The video resolution in the JSON ({regionInfo.VideoWidth}x{regionInfo.VideoHeight}) " +
                    $"does not match the current video resolution ({_videoResolution.Width}x{_videoResolution.Height}).\n\n" +
                    $"Continue applying the selection? (coordinates may be inaccurate)",
                    "Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            // Update the image boundaries
            UpdateImageBounds();

            // Calculate the scale ratio (from video coordinates to Canvas coordinates)
            double scaleX = _imageBounds.Width / _videoResolution.Width;
            double scaleY = _imageBounds.Height / _videoResolution.Height;

            // Convert coordinates
            double canvasX = _imageBounds.Left + regionInfo.X * scaleX;
            double canvasY = _imageBounds.Top + regionInfo.Y * scaleY;
            double canvasW = regionInfo.Width * scaleX;
            double canvasH = regionInfo.Height * scaleY;

            // Set the selection
            Canvas.SetLeft(SelectionRect, canvasX);
            Canvas.SetTop(SelectionRect, canvasY);
            SelectionRect.Width = canvasW;
            SelectionRect.Height = canvasH;
            SelectionRect.Visibility = Visibility.Visible;

            // Update the handles and status
            UpdateHandles();
            Confirm.IsEnabled = true;
            Clear.IsEnabled = true;
            ProcessVideo.IsEnabled = true;

            // Update the display
            SelectedRegion = new System.Drawing.Rectangle(regionInfo.X, regionInfo.Y, regionInfo.Width, regionInfo.Height);
            UpdateInfoDisplay(new System.Windows.Point(canvasX, canvasY));
        }

        private System.Drawing.Rectangle GetSelectedRegionInVideoSpace()
        {
            if (SelectionRect.Visibility != Visibility.Visible)
                return new System.Drawing.Rectangle();

            // Use the calculated image boundaries
            if (_imageBounds.Width <= 0 || _imageBounds.Height <= 0)
            {
                UpdateImageBounds();
            }

            // Calculate the scale ratio
            double scaleX = _videoResolution.Width / _imageBounds.Width;
            double scaleY = _videoResolution.Height / _imageBounds.Height;

            // Get the coordinates of the selection box relative to the image boundaries
            double selX = Canvas.GetLeft(SelectionRect) - _imageBounds.Left;
            double selY = Canvas.GetTop(SelectionRect) - _imageBounds.Top;
            double selW = SelectionRect.Width;
            double selH = SelectionRect.Height;

            // Convert back to the original video coordinates
            int x = (int)Math.Round(selX * scaleX);
            int y = (int)Math.Round(selY * scaleY);
            int w = (int)Math.Round(selW * scaleX);
            int h = (int)Math.Round(selH * scaleY);

            // Boundary protection
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

            // 1. Determine the pixel format
            PixelFormat pixelFormat;

            // The Type of OpenCvSharp is usually:
            // CV_8UC1 (gray), CV_8UC3 (BGR), CV_8UC4 (BGRA)
            switch (mat.Type().ToString())
            {
                case "CV_8UC1":
                    pixelFormat = PixelFormats.Gray8;
                    break;
                case "CV_8UC3":
                    pixelFormat = PixelFormats.Bgr24; // OpenCV defaults to BGR
                    break;
                case "CV_8UC4":
                    pixelFormat = PixelFormats.Bgra32; // With Alpha channel
                    break;
                default:
                    throw new ArgumentException($"Unsupported Mat type: {mat.Type()}");
            }

            // 2. Calculate the image data size
            // stride (stride) = the number of bytes per row (including padding)
            int stride = (int)mat.Step();
            int size = (int)mat.Total() * mat.ElemSize();

            // 3. Create BitmapSource
            // Use the Create method to create directly from the memory pointer, avoiding the intermediate loss of converting to System.Drawing.Bitmap
            BitmapSource bitmapSource = BitmapSource.Create(
                mat.Width,
                mat.Height,
                96d, 96d, // DPI setting, usually set to 96
                pixelFormat,
                null, // Palette
                mat.Data, // Use the data pointer of the Mat directly
                size,
                stride
            );

            // 4. Freeze the object (important)
            // This allows the BitmapSource to be accessed by the UI thread after it is created in a non-UI thread
            bitmapSource.Freeze();

            return bitmapSource;
        }

        private void ProcessVideo_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_videoPath))
            {
                MessageBox.Show("Please open the video file first", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (SelectedRegion.Width <= 0 || SelectedRegion.Height <= 0)
            {
                MessageBox.Show("Please select a valid region first", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Get the setting parameters
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

            // Get the processing range
            bool limitToFirstMinute = ProcessFirstMinute.IsChecked == true;

            // Generate the subtitle file name (the same as the video file name)
            string videoDir = System.IO.Path.GetDirectoryName(_videoPath);
            string videoName = System.IO.Path.GetFileNameWithoutExtension(_videoPath);
            string srtPath = System.IO.Path.Combine(videoDir, $"{videoName}.srt");

            // Clear the previous subtitle list
            Subtitles.Clear();
            _currentSrtEntries.Clear();

            // Disable the processing button, show the progress panel
            ProcessVideo.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressBar.Value = 0;
            ProgressStatusText.Text = "Processing...";
            ExportSrtButton.Visibility = Visibility.Collapsed;

            // Run in the background thread (to avoid blocking the UI)
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
                            // Update the progress
                            if (info.TotalTime > 0)
                            {
                                double progressPercent = (info.CurrentTime / info.TotalTime) * 100;
                                ProgressBar.Value = Math.Min(100, Math.Max(0, progressPercent));
                            }

                            // Update the speed display
                            ProgressSpeedText.Text = $"[x{info.SpeedRatio:F1}]";
                            if (info.SpeedRatio > 1)
                                ProgressSpeedText.Foreground = new SolidColorBrush(Colors.Green);
                            else
                                ProgressSpeedText.Foreground = new SolidColorBrush(Colors.Red);

                            // Update the time display
                            var currentSpan = TimeSpan.FromSeconds(info.CurrentTime);
                            var totalSpan = TimeSpan.FromSeconds(info.TotalTime);
                            ProgressCurrentTimeText.Text = FormatTimeSpan(currentSpan);
                            ProgressTotalTimeText.Text = FormatTimeSpan(totalSpan);

                            // Add new subtitle to the list (using AddOrMergeSubtitle for filtering and merging)
                            if (info.LatestSubtitle != null)
                            {
                                // Use the AddOrMergeSubtitle method for filtering and merging
                                int entriesCountBefore = _currentSrtEntries.Count;
                                var mergedEntry = AddOrMergeSubtitle(_currentSrtEntries,
                                    info.LatestSubtitle.Text,
                                    info.LatestSubtitle.StartTime.TotalSeconds,
                                    info.LatestSubtitle.EndTime.TotalSeconds);

                                // If null is returned, it means it has been filtered out, skip
                                if (mergedEntry == null)
                                {
                                    return; // Use return instead of continue in lambda
                                }

                                // Check if it is merged into an existing entry or a new entry
                                // If the number of entries does not increase, it means it is merged into an existing entry
                                bool isNewEntry = (_currentSrtEntries.Count > entriesCountBefore);

                                if (!isNewEntry)
                                {
                                    // Merge into an existing entry, update the UI
                                    int entryIndex = _currentSrtEntries.IndexOf(mergedEntry);
                                    if (entryIndex >= 0 && entryIndex < Subtitles.Count)
                                    {
                                        var existingItem = Subtitles[entryIndex];
                                        existingItem.TimeRange = $"{FormatTimeSpan(mergedEntry.StartTime)} --> {FormatTimeSpan(mergedEntry.EndTime)}";
                                        existingItem.EndTimeSeconds = mergedEntry.EndTime.TotalSeconds;

                                        // Update the text (if the text has changed)
                                        var newLines = mergedEntry.Text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                                        if (newLines.Count == 0) newLines.Add(mergedEntry.Text); // If there is no line break, keep the original

                                        if (newLines.Count != existingItem.Lines.Count ||
                                            !newLines.SequenceEqual(existingItem.Lines))
                                        {
                                            existingItem.Lines.Clear();
                                            existingItem.Lines.AddRange(newLines);
                                        }

                                        // Refresh the display
                                        var collectionView = System.Windows.Data.CollectionViewSource.GetDefaultView(Subtitles);
                                        collectionView.Refresh();
                                    }
                                }
                                else
                                {
                                    // New entry, add to the UI
                                    var newLines = mergedEntry.Text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                                    if (newLines.Count == 0) newLines.Add(mergedEntry.Text); // If there is no line break, keep the original

                                    var subtitleItem = new SubtitleItem
                                    {
                                        TimeRange = $"{FormatTimeSpan(mergedEntry.StartTime)} --> {FormatTimeSpan(mergedEntry.EndTime)}",
                                        Lines = newLines,
                                        StartTimeSeconds = mergedEntry.StartTime.TotalSeconds,
                                        EndTimeSeconds = mergedEntry.EndTime.TotalSeconds
                                    };
                                    Subtitles.Add(subtitleItem);
                                }

                                // Automatically scroll to the bottom
                                if (SubtitleListBox.Items.Count > 0)
                                {
                                    SubtitleListBox.ScrollIntoView(SubtitleListBox.Items[SubtitleListBox.Items.Count - 1]);
                                }
                            }

                            // Processing completed
                            if (info.IsFinished)
                            {
                                ProgressStatusText.Text = "Processing completed";
                                ProgressBar.Value = 100;
                                ProgressSpeedText.Foreground = new SolidColorBrush(Colors.Green);
                                ExportSrtButton.Visibility = Visibility.Visible;
                                ProcessVideo.IsEnabled = true;
                            }
                        });
                    });

                    generator.GenerateSrt(engine, srtPath, progress);

                    Logger.Log.Info($"Subtitles generated successfully!\nSave location: {srtPath}\nSubtitle count: {Subtitles.Count}");
                }
                catch (Exception ex)
                {
                    Logger.Log.Error($"Processing failed: {ex.Message}");
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
            // Check if the clicked object is a TextBox
            var source = e.OriginalSource as System.Windows.DependencyObject;
            while (source != null)
            {
                if (source is TextBox)
                {
                    // The clicked object is a TextBox, do not process the selection event, let the TextBox get the focus
                    _isEditingSubtitle = true;
                    return;
                }
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            }
            _isEditingSubtitle = false;
        }

        private void SubtitleListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Avoid triggering a jump when editing
            if (e.AddedItems.Count == 0 || _isEditingSubtitle) return;

            if (SubtitleListBox.SelectedItem is SubtitleItem item && !string.IsNullOrEmpty(_videoPath))
            {
                // Jump to the subtitle end time (because the start time may not have fully displayed the subtitle)
                LoadFrameAtTime(_videoPath, item.EndTimeSeconds);
            }
        }

        private void SubtitleTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            // Get the original text (DataContext)
            var originalText = textBox.DataContext as string;
            if (originalText == null) return;

            // Find the ListBoxItem upwards to get the SubtitleItem
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
                    // Update the subtitle text
                    var newText = textBox.Text;
                    var lineIndex = subtitleItem.Lines.IndexOf(originalText);

                    if (lineIndex >= 0)
                    {
                        subtitleItem.Lines[lineIndex] = newText;

                        // Synchronize update _currentSrtEntries
                        var subtitleIndex = Subtitles.IndexOf(subtitleItem);
                        if (subtitleIndex >= 0 && subtitleIndex < _currentSrtEntries.Count)
                        {
                            _currentSrtEntries[subtitleIndex].Text = string.Join("\n", subtitleItem.Lines);
                        }
                    }
                }
            }

            // Editing completed, reset the flag
            _isEditingSubtitle = false;
        }

        private void DeleteSubtitleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (SubtitleListBox.SelectedItems.Count == 0)
            {
                return;
            }

            // Get all selected items, convert to SubtitleItem list
            var selectedItems = SubtitleListBox.SelectedItems.Cast<SubtitleItem>().ToList();

            if (selectedItems.Count == 0)
            {
                return;
            }

            // Get the indices to delete (from largest to smallest to avoid the problem of index change when deleting)
            var indicesToDelete = selectedItems
                .Select(item => Subtitles.IndexOf(item))
                .Where(index => index >= 0 && index < _currentSrtEntries.Count)
                .OrderByDescending(index => index)
                .ToList();

            if (indicesToDelete.Count == 0)
            {
                return;
            }

            // Confirm deletion
            string message = indicesToDelete.Count == 1
                ? "Are you sure you want to delete the selected subtitles?"
                : $"Are you sure you want to delete the selected {indicesToDelete.Count} subtitles?";

            if (MessageBox.Show(message, "Confirm deletion", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes)
            {
                return;
            }

            // Delete from the back to avoid index change
            foreach (var index in indicesToDelete)
            {
                if (index >= 0 && index < Subtitles.Count)
                {
                    Subtitles.RemoveAt(index);
                }
                if (index >= 0 && index < _currentSrtEntries.Count)
                {
                    _currentSrtEntries.RemoveAt(index);
                }
            }

            // If the list is empty after deletion, hide the export button
            if (_currentSrtEntries.Count == 0)
            {
                ExportSrtButton.Visibility = Visibility.Collapsed;
            }

            // Update the indices of the subsequent items (if needed)
            // Note: The Index property of SubtitleItem will be recalculated when exporting, so it does not need to be updated here
        }

        private void ToggleSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            _keepSelectionVisible = !_keepSelectionVisible;

            if (_keepSelectionVisible)
            {
                // Show the selection
                if (SelectedRegion.Width > 0 && SelectedRegion.Height > 0)
                {
                    // If there is already a selection, show it
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
                    // If there is no selection, prompt the user
                    MessageBox.Show("Please select a region first", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    _keepSelectionVisible = false;
                    ToggleSelectionButton.Content = "Show selection";
                    return;
                }
                ToggleSelectionButton.Content = "Hide selection";
            }
            else
            {
                // Hide the selection
                SelectionRect.Visibility = Visibility.Collapsed;
                UpdateHandles();
                InfoBorder.Visibility = Visibility.Collapsed;
                ToggleSelectionButton.Content = "Show selection";
            }
        }

        private void ExportSrtButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSrtEntries.Count == 0)
            {
                MessageBox.Show("No subtitles to export", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Subtitle file|*.srt|All files|*.*",
                FileName = System.IO.Path.GetFileNameWithoutExtension(_videoPath) + ".srt"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    WriteSrtFile(dialog.FileName, _currentSrtEntries);
                    MessageBox.Show($"Subtitles exported to: {dialog.FileName}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            // Keep this method for backward compatibility, but actually use ProcessVideo_Click
            ProcessVideo_Click(null, null);
        }

        // Check if the text contains Chinese, Japanese or English
        private bool ContainsValidLanguage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            bool hasChinese = false;
            bool hasJapanese = false;
            bool hasEnglish = false;

            foreach (char c in text)
            {
                // Check Chinese (CJK unified Han characters)
                if (c >= 0x4E00 && c <= 0x9FFF)
                {
                    hasChinese = true;
                }
                // Check Japanese hiragana
                else if (c >= 0x3040 && c <= 0x309F)
                {
                    hasJapanese = true;
                }
                // Check Japanese katakana
                else if (c >= 0x30A0 && c <= 0x30FF)
                {
                    hasJapanese = true;
                }
                // Check English
                else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                {
                    hasEnglish = true;
                }
            }

            return hasChinese || hasJapanese || hasEnglish;
        }

        // Improved similarity calculation: check if the text contains or overlaps significantly
        private bool IsTextSimilar(string text1, string text2, double threshold = 0.8)
        {
            if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
                return false;

            // 1. Completely the same
            if (text1 == text2)
                return true;

            // 2. Check if one contains the other (handle prefix/suffix cases)
            if (text1.Contains(text2) || text2.Contains(text1))
            {
                return true;
            }

            // 3. Use the edit distance to calculate the similarity
            var similarity = CalculateLevenshteinSimilarity(text1, text2);
            return similarity >= threshold;
        }

        // Subtitle merging logic copied from VideoProcessor
        private SrtEntry AddOrMergeSubtitle(List<SrtEntry> entries, string text, double start, double end)
        {
            // Filter: if the subtitle does not contain English, Chinese or Japanese, filter it out
            if (!ContainsValidLanguage(text))
            {
                // Return null means it is filtered out, not added to the list
                return null;
            }

            if (entries.Count > 0)
            {
                var last = entries[entries.Count - 1];

                // 1. The text is completely the same, merge
                if (last.Text == text)
                {
                    last.EndTime = TimeSpan.FromSeconds(end);
                    return last;
                }

                // 2. The text similarity is high, and the time overlaps or is adjacent, merge
                double gap = start - last.EndTime.TotalSeconds;
                if (gap < 2.0 && IsTextSimilar(last.Text, text, 0.8))
                {
                    // Similar merge, take the longer one
                    if (text.Length > last.Text.Length) last.Text = text;
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
