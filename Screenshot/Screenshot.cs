using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Size = System.Drawing.Size;
using System.Windows.Controls;
using System.Windows.Media;
using System.Linq;

namespace Screenshot
{
    // 扩展ScreenScale，增加屏幕边界信息
    public class ScreenScale
    {
        public double ScaleX { get; set; }
        public double ScaleY { get; set; }
        public Rect PhysicalBounds { get; set; } // 屏幕物理像素边界
        public Rect LogicalBounds { get; set; }  // 屏幕逻辑像素边界
        public bool IsPrimary { get; set; }      // 是否为主屏幕
    }

    public static class Screenshot
    {
        /// <summary>
        /// 捕获所有屏幕（适配每个屏幕的真实缩放比例，解决多屏幕缩放不一致问题）
        /// </summary>
        public static BitmapSource CaptureAllScreens()
        {
            var screens = Screen.AllScreens;
            var screenScaleList = new List<ScreenScale>();
            double mainScaleX = 1.0;
            double mainScaleY = 1.0;

            // 计算虚拟屏幕的真实边界（物理像素）
            double minLeft = double.MaxValue;
            double minTop = double.MaxValue;
            double maxRight = double.MinValue;
            double maxBottom = double.MinValue;

            foreach (var screen in screens)
            {
                // 获取屏幕的DPI缩放比例（精准获取当前屏幕DPI）
                ScreenScale scale = GetScreenScale(screen);
                screenScaleList.Add(scale);

                // 记录虚拟屏幕的物理边界
                minLeft = Math.Min(minLeft, scale.PhysicalBounds.Left);
                minTop = Math.Min(minTop, scale.PhysicalBounds.Top);
                maxRight = Math.Max(maxRight, scale.PhysicalBounds.Right);
                maxBottom = Math.Max(maxBottom, scale.PhysicalBounds.Bottom);

                // 记录主屏幕缩放
                if (screen.Primary)
                {
                    mainScaleX = scale.ScaleX;
                    mainScaleY = scale.ScaleY;
                }
            }

            // 分屏幕捕获并拼接（核心修复：解决多屏幕缩放不一致）
            var virtualPhysRect = new Rect(minLeft, minTop, maxRight - minLeft, maxBottom - minTop);
            BitmapSource physicalBitmap = CaptureMultiScreen(virtualPhysRect, screenScaleList);

            // 缩放位图到WPF逻辑尺寸（和屏幕视觉一致）
            var scaleTransform = new ScaleTransform(1 / mainScaleX, 1 / mainScaleY);
            var scaledBitmap = new TransformedBitmap(physicalBitmap, scaleTransform);
            scaledBitmap.Freeze(); // 冻结以提升性能

            return scaledBitmap;
        }

        /// <summary>
        /// 分屏幕捕获并拼接成完整虚拟屏幕位图（解决多屏幕缩放不一致）
        /// </summary>
        private static BitmapSource CaptureMultiScreen(Rect virtualPhysRect, List<ScreenScale> screenScales)
        {
            using (var totalBitmap = new Bitmap(
                (int)virtualPhysRect.Width,
                (int)virtualPhysRect.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(totalBitmap))
                {
                    // 逐个屏幕捕获并绘制到总位图
                    foreach (var scale in screenScales)
                    {
                        var screenPhys = scale.PhysicalBounds;
                        // 计算当前屏幕在总位图中的偏移
                        int destX = (int)(screenPhys.Left - virtualPhysRect.Left);
                        int destY = (int)(screenPhys.Top - virtualPhysRect.Top);

                        // 捕获当前屏幕的物理像素
                        using (var screenBitmap = new Bitmap(
                            (int)screenPhys.Width,
                            (int)screenPhys.Height,
                            System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                        {
                            using (var screenG = Graphics.FromImage(screenBitmap))
                            {
                                screenG.CopyFromScreen(
                                    (int)screenPhys.Left, (int)screenPhys.Top,
                                    0, 0, screenBitmap.Size,
                                    CopyPixelOperation.SourceCopy);
                            }
                            // 绘制到总位图对应位置
                            g.DrawImage(screenBitmap, destX, destY);
                        }
                    }
                }

                // 转换为WPF BitmapSource
                IntPtr hBitmap = totalBitmap.GetHbitmap();
                var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                DeleteObject(hBitmap);
                return bitmapSource;
            }
        }

        /// <summary>
        /// 获取指定屏幕的DPI缩放比例（使用 Shcore.dll 精准获取 Per-Monitor DPI）
        /// </summary>
        private static ScreenScale GetScreenScale(Screen screen)
        {
            ScreenScale scale = new ScreenScale();
            scale.PhysicalBounds = new Rect(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height);
            scale.IsPrimary = screen.Primary;

            try
            {
                // 1. 获取显示器句柄 (HMONITOR)
                // 取屏幕中心点来确保获取到正确的显示器句柄
                var centerPoint = new System.Drawing.Point(
                    screen.Bounds.Left + (screen.Bounds.Width / 2),
                    screen.Bounds.Top + (screen.Bounds.Height / 2));

                IntPtr hMonitor = MonitorFromPoint(centerPoint, MONITOR_DEFAULTTONEAREST);

                // 2. 通过 Shcore.dll 获取该显示器的真实 DPI
                uint dpiX, dpiY;
                int result = GetDpiForMonitor(hMonitor, MonitorDpiType.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);

                if (result == 0) // S_OK
                {
                    scale.ScaleX = dpiX / 96.0;
                    scale.ScaleY = dpiY / 96.0;
                }
                else
                {
                    // 如果 API 失败（例如系统不支持），回退到通用逻辑
                    scale.ScaleX = 1.0;
                    scale.ScaleY = 1.0;
                    DebugLogger.Log($"获取DPI失败，回退到1.0。Screen: {screen.DeviceName}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"获取DPI异常: {ex.Message}。回退到1.0");
                scale.ScaleX = 1.0;
                scale.ScaleY = 1.0;
            }

            return scale;
        }

        // ========================================================================
        // 下面是必须引入的 Win32 API
        // ========================================================================

        private const int MONITOR_DEFAULTTONEAREST = 2;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, int dwFlags);

        [System.Runtime.InteropServices.DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

        private enum MonitorDpiType
        {
            MDT_EFFECTIVE_DPI = 0,
            MDT_ANGULAR_DPI = 1,
            MDT_RAW_DPI = 2,
        }

        // 获取设备DPI的Win32 API（精准获取指定屏幕DPI）
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        /// <summary>
        /// 捕获指定区域（物理像素坐标）
        /// </summary>
        public static BitmapSource CaptureRegion(Rect region)
        {
            using (var bitmap = new System.Drawing.Bitmap(
                (int)region.Width,
                (int)region.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = System.Drawing.Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(
                        (int)region.Left,
                        (int)region.Top,
                        0, 0,
                        bitmap.Size,
                        System.Drawing.CopyPixelOperation.SourceCopy);
                }

                // 转换为WPF的BitmapSource
                var hBitmap = bitmap.GetHbitmap();
                var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                DeleteObject(hBitmap); // 释放非托管资源
                return bitmapSource;
            }
        }

        /// <summary>
        /// 获取用户选择的区域
        /// </summary>
        public static Rect GetRegion()
        {
            DebugLogger.Log("========== 开始新的截图会话 ==========");

            var options = new ScreenshotOptions();

            // 1. 再次遍历屏幕以获取 物理边界起点(minLeft/Top) 和 主屏幕缩放(MainScale)
            // 必须与 CaptureAllScreens 中的逻辑完全一致
            var screens = Screen.AllScreens;
            double minLeft = double.MaxValue;
            double minTop = double.MaxValue;
            double maxRight = double.MinValue;
            double maxBottom = double.MinValue;
            double mainScaleX = 1.0;
            double mainScaleY = 1.0;

            foreach (var screen in screens)
            {
                var scale = GetScreenScale(screen);

                DebugLogger.Log($"屏幕: {screen.DeviceName}, 主屏: {screen.Primary}, Bounds: {screen.Bounds}, Scale: {scale.ScaleX:F2},{scale.ScaleY:F2}");

                if (screen.Primary)
                {
                    mainScaleX = scale.ScaleX;
                    mainScaleY = scale.ScaleY;
                }

                minLeft = Math.Min(minLeft, screen.Bounds.Left);
                minTop = Math.Min(minTop, screen.Bounds.Top);
                maxRight = Math.Max(maxRight, screen.Bounds.Right);
                maxBottom = Math.Max(maxBottom, screen.Bounds.Bottom);
            }

            DebugLogger.Log($"虚拟屏幕物理边界: Left={minLeft}, Top={minTop}, Right={maxRight}, Bottom={maxBottom}");
            DebugLogger.Log($"主屏幕缩放比例: X={mainScaleX}, Y={mainScaleY}");

            // 2. 捕获全屏图像
            var bitmap = CaptureAllScreens();

            // 3. 计算用于显示窗口的逻辑坐标
            // 窗口的 (0,0) 对应物理像素的 (minLeft, minTop)
            double windowLeft = minLeft / mainScaleX;
            double windowTop = minTop / mainScaleY;
            double windowWidth = (maxRight - minLeft) / mainScaleX;
            double windowHeight = (maxBottom - minTop) / mainScaleY;

            DebugLogger.Log($"遮罩窗口逻辑坐标(WPF): Left={windowLeft}, Top={windowTop}, Width={windowWidth}, Height={windowHeight}");

            var window = new RegionSelectionWindow
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                ShowInTaskbar = false,
                BorderThickness = new Thickness(0),
                BackgroundImage =
                {
                    Source = bitmap,
                    Opacity = options.BackgroundOpacity
                },
                InnerBorder = { BorderBrush = options.SelectionRectangleBorderBrush },
                // 关键：确保窗口位置覆盖所有屏幕，且坐标系对齐
                Left = windowLeft,
                Top = windowTop,
                Width = windowWidth,
                Height = windowHeight
            };

            window.ShowDialog();

            if (window.SelectedRegion == null)
            {
                DebugLogger.Log("用户取消了截图");
                return Rect.Empty;
            }

            // logicalRegion 是相对于 Window (0,0) 的坐标
            // 或者是相对于 Screen 的坐标？这取决于 RegionSelectionWindow 的实现。
            // 假设 SelectedRegion 返回的是 **相对于 Window Client Area** 的坐标 (通常是 Canvas.Left/Top)
            var selectionInWindow = window.SelectedRegion.Value;

            DebugLogger.Log($"用户选区(相对于窗口): X={selectionInWindow.X}, Y={selectionInWindow.Y}, W={selectionInWindow.Width}, H={selectionInWindow.Height}");

            // ============================================================
            // 核心修复逻辑
            // ============================================================

            // 1. 还原为 "相对于全屏位图" 的物理像素尺寸
            // 因为背景图是统一按 MainScale 缩小的，所以统一按 MainScale 放大回去
            double physicalX_Relative = selectionInWindow.X * mainScaleX;
            double physicalY_Relative = selectionInWindow.Y * mainScaleY;
            double physicalW = selectionInWindow.Width * mainScaleX;
            double physicalH = selectionInWindow.Height * mainScaleY;

            // 2. 加上 "全屏位图" 在真实物理世界中的起始偏移量 (minLeft, minTop)
            // 这样才能得到 CopyFromScreen 需要的绝对物理坐标
            double finalPhysicalX = minLeft + physicalX_Relative;
            double finalPhysicalY = minTop + physicalY_Relative;

            var finalRect = new Rect(finalPhysicalX, finalPhysicalY, physicalW, physicalH);

            DebugLogger.Log($"计算出的物理区域(CopyFromScreen): X={finalRect.X}, Y={finalRect.Y}, W={finalRect.Width}, H={finalRect.Height}");

            return finalRect;
        }

        // 释放非托管资源
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}