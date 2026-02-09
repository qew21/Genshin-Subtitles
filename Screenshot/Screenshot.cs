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
    // Extend ScreenScale, add screen boundary information
    public class ScreenScale
    {
        public double ScaleX { get; set; }
        public double ScaleY { get; set; }
        public Rect PhysicalBounds { get; set; } // Screen physical pixel boundary
        public Rect LogicalBounds { get; set; }  // Screen logical pixel boundary
        public bool IsPrimary { get; set; }      // Whether it is the main screen
    }

    public static class Screenshot
    {
        /// <summary>
        /// Capture all screens (adapt to the real scale of each screen, solve the problem of inconsistent scaling of multiple screens)
        /// </summary>
        public static BitmapSource CaptureAllScreens()
        {
            var screens = Screen.AllScreens;
            var screenScaleList = new List<ScreenScale>();
            double mainScaleX = 1.0;
            double mainScaleY = 1.0;

            // Calculate the real boundary of the virtual screen (physical pixels)
            double minLeft = double.MaxValue;
            double minTop = double.MaxValue;
            double maxRight = double.MinValue;
            double maxBottom = double.MinValue;

            foreach (var screen in screens)
            {
                // Get the DPI scale of the screen (accurately get the DPI of the current screen)
                ScreenScale scale = GetScreenScale(screen);
                screenScaleList.Add(scale);

                // Record the physical boundary of the virtual screen
                minLeft = Math.Min(minLeft, scale.PhysicalBounds.Left);
                minTop = Math.Min(minTop, scale.PhysicalBounds.Top);
                maxRight = Math.Max(maxRight, scale.PhysicalBounds.Right);
                maxBottom = Math.Max(maxBottom, scale.PhysicalBounds.Bottom);

                // Record the main screen scale
                if (screen.Primary)
                {
                    mainScaleX = scale.ScaleX;
                    mainScaleY = scale.ScaleY;
                }
            }

            // Capture and stitch by screen (core fix: solve the problem of inconsistent scaling of multiple screens)
            var virtualPhysRect = new Rect(minLeft, minTop, maxRight - minLeft, maxBottom - minTop);
            BitmapSource physicalBitmap = CaptureMultiScreen(virtualPhysRect, screenScaleList);

            // Scale the bitmap to the WPF logical size (consistent with the screen visual)
            var scaleTransform = new ScaleTransform(1 / mainScaleX, 1 / mainScaleY);
            var scaledBitmap = new TransformedBitmap(physicalBitmap, scaleTransform);
            scaledBitmap.Freeze(); // Freeze to improve performance

            return scaledBitmap;
        }

        /// <summary>
        /// Capture and stitch by screen to form a complete virtual screen bitmap (solve the problem of inconsistent scaling of multiple screens)
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
                    // Capture and draw to the total bitmap one by one
                    foreach (var scale in screenScales)
                    {
                        var screenPhys = scale.PhysicalBounds;
                        // Calculate the offset of the current screen in the total bitmap
                        int destX = (int)(screenPhys.Left - virtualPhysRect.Left);
                        int destY = (int)(screenPhys.Top - virtualPhysRect.Top);

                        // Capture the physical pixels of the current screen
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
                            // Draw to the corresponding position in the total bitmap
                            g.DrawImage(screenBitmap, destX, destY);
                        }
                    }
                }

                // Convert to WPF BitmapSource
                IntPtr hBitmap = totalBitmap.GetHbitmap();
                var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                DeleteObject(hBitmap);
                return bitmapSource;
            }
        }

        /// <summary>
        /// Get the DPI scale of the specified screen (accurately get the DPI of the specified screen using Shcore.dll)
        /// </summary>
        private static ScreenScale GetScreenScale(Screen screen)
        {
            ScreenScale scale = new ScreenScale();
            scale.PhysicalBounds = new Rect(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height);
            scale.IsPrimary = screen.Primary;

            try
            {
                // 1. Get the display handle (HMONITOR)
                // Take the screen center point to ensure that the correct display handle is obtained
                var centerPoint = new System.Drawing.Point(
                    screen.Bounds.Left + (screen.Bounds.Width / 2),
                    screen.Bounds.Top + (screen.Bounds.Height / 2));

                IntPtr hMonitor = MonitorFromPoint(centerPoint, MONITOR_DEFAULTTONEAREST);

                // 2. Get the real DPI of the displayer through Shcore.dll
                uint dpiX, dpiY;
                int result = GetDpiForMonitor(hMonitor, MonitorDpiType.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);

                if (result == 0) // S_OK
                {
                    scale.ScaleX = dpiX / 96.0;
                    scale.ScaleY = dpiY / 96.0;
                }
                else
                {
                    // If the API fails (for example, the system does not support it), fall back to the general logic
                    scale.ScaleX = 1.0;
                    scale.ScaleY = 1.0;
                    DebugLogger.Log($"Failed to get DPI, falling back to 1.0. Screen: {screen.DeviceName}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Exception getting DPI: {ex.Message}. Falling back to 1.0");
                scale.ScaleX = 1.0;
                scale.ScaleY = 1.0;
            }

            return scale;
        }

        // ========================================================================
        // The following is the Win32 API that must be imported
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

        // Get the Win32 API for the device DPI (accurately get the DPI of the specified screen)
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        /// <summary>
        /// Capture the specified region (physical pixel coordinates)
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

                // Convert to WPF BitmapSource
                var hBitmap = bitmap.GetHbitmap();
                var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                DeleteObject(hBitmap); // Release unmanaged resources
                return bitmapSource;
            }
        }

        /// <summary>
        /// Get the user's selected region
        /// </summary>
        public static Rect GetRegion()
        {
            DebugLogger.Log("========== Start a new screenshot session ==========");

            var options = new ScreenshotOptions();

            // 1. Loop through the screens again to get the physical boundary start point (minLeft/Top) and the main screen scale (MainScale)
            // Must be completely consistent with the logic in CaptureAllScreens
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

                DebugLogger.Log($"Screen: {screen.DeviceName}, Main screen: {screen.Primary}, Bounds: {screen.Bounds}, Scale: {scale.ScaleX:F2},{scale.ScaleY:F2}");

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

            DebugLogger.Log($"Virtual screen physical boundary: Left={minLeft}, Top={minTop}, Right={maxRight}, Bottom={maxBottom}");
            DebugLogger.Log($"Main screen scale: X={mainScaleX}, Y={mainScaleY}");

            // 2. Capture the full screen image
            var bitmap = CaptureAllScreens();

            // 3. Calculate the logical coordinates for the window to display
            // The (0,0) of the window corresponds to the physical pixels of (minLeft, minTop)
            double windowLeft = minLeft / mainScaleX;
            double windowTop = minTop / mainScaleY;
            double windowWidth = (maxRight - minLeft) / mainScaleX;
            double windowHeight = (maxBottom - minTop) / mainScaleY;

            DebugLogger.Log($"Mask window logical coordinates (WPF): Left={windowLeft}, Top={windowTop}, Width={windowWidth}, Height={windowHeight}");

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
                // Important: ensure that the window position covers all screens and the coordinate system is aligned
                Left = windowLeft,
                Top = windowTop,
                Width = windowWidth,
                Height = windowHeight
            };

            window.ShowDialog();

            if (window.SelectedRegion == null)
            {
                DebugLogger.Log("User cancelled the screenshot");
                return Rect.Empty;
            }

            // logicalRegion is the coordinates relative to the Window (0,0)
            // or is it relative to the Screen coordinates? This depends on the implementation of RegionSelectionWindow.
            // Assume that SelectedRegion returns the coordinates relative to the Window Client Area (usually Canvas.Left/Top)
            var selectionInWindow = window.SelectedRegion.Value;

            DebugLogger.Log($"User selected region (relative to the window): X={selectionInWindow.X}, Y={selectionInWindow.Y}, W={selectionInWindow.Width}, H={selectionInWindow.Height}");

            // ============================================================
            // Core fix logic
            // ============================================================

            // 1. Restore to the physical pixel size of "relative to the full screen bitmap"
            // Because the background image is uniformly scaled down by MainScale, so uniformly scaled back by MainScale
            double physicalX_Relative = selectionInWindow.X * mainScaleX;
            double physicalY_Relative = selectionInWindow.Y * mainScaleY;
            double physicalW = selectionInWindow.Width * mainScaleX;
            double physicalH = selectionInWindow.Height * mainScaleY;

            // 2. Add the starting offset of the "full screen bitmap" in the real physical world (minLeft, minTop)
            // This is to get the absolute physical coordinates needed by CopyFromScreen
            double finalPhysicalX = minLeft + physicalX_Relative;
            double finalPhysicalY = minTop + physicalY_Relative;

            var finalRect = new Rect(finalPhysicalX, finalPhysicalY, physicalW, physicalH);

            DebugLogger.Log($"Calculated physical region (CopyFromScreen): X={finalRect.X}, Y={finalRect.Y}, W={finalRect.Width}, H={finalRect.Height}");

            return finalRect;
        }

        // Release unmanaged resources
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}