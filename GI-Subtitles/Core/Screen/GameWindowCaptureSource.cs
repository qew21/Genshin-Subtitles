using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace GI_Subtitles.Core.Screen
{
    /// <summary>
    /// Captures a game HWND with Windows.Graphics.Capture and returns client-coordinate
    /// crops as 24-bit bitmaps for the existing OCR pipeline.
    /// </summary>
    public sealed class GameWindowCaptureSource : IDisposable
    {
        private const int MinimumWindowsBuild = 18362;
        private const int MaxFramesToDrainPerRequest = 3;

        private static readonly Guid GraphicsCaptureItemGuid =
            new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        private static readonly Guid Id3D11Texture2DGuid =
            new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        private readonly object _sync = new object();
        private Device _device;
        private DeviceContext _context;
        private IDirect3DDevice _winRtDevice;
        private GraphicsCaptureItem _item;
        private Direct3D11CaptureFramePool _framePool;
        private GraphicsCaptureSession _session;
        private Texture2D _latestTexture;
        private SizeInt32 _poolSize;
        private volatile bool _captureClosed;
        private bool _disposed;

        private GameWindowCaptureSource(IntPtr windowHandle)
        {
            Initialize(windowHandle);
        }

        public static bool IsSupportedOperatingSystem()
        {
            var version = new NativeOsVersionInfo
            {
                Size = Marshal.SizeOf(typeof(NativeOsVersionInfo)),
                ServicePack = new string('\0', 128)
            };

            return RtlGetVersion(ref version) == 0 &&
                   version.MajorVersion >= 10 &&
                   version.BuildNumber >= MinimumWindowsBuild;
        }

        public static GameWindowCaptureSource Create(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                throw new ArgumentException("A game window handle is required.", nameof(windowHandle));
            }

            if (!IsSupportedOperatingSystem())
            {
                throw new PlatformNotSupportedException(
                    "Windows Graphics Capture for an HWND requires Windows 10 build 18362 or later.");
            }

            if (!GraphicsCaptureSession.IsSupported())
            {
                throw new PlatformNotSupportedException(
                    "Windows Graphics Capture is not supported by this device.");
            }

            return new GameWindowCaptureSource(windowHandle);
        }

        /// <summary>
        /// Captures a rectangle expressed in desktop client coordinates. The frame pool
        /// stays alive across OCR ticks; only the requested crop is copied to CPU memory.
        /// </summary>
        public Bitmap Capture(
            Rectangle requestedScreenBounds,
            Rectangle clientBounds,
            Rectangle windowBounds)
        {
            if (requestedScreenBounds.Width <= 0 || requestedScreenBounds.Height <= 0 ||
                clientBounds.Width <= 0 || clientBounds.Height <= 0)
            {
                return null;
            }

            lock (_sync)
            {
                ThrowIfDisposed();
                if (_captureClosed)
                {
                    return null;
                }

                DrainAvailableFrames();
                if (_latestTexture == null)
                {
                    return null;
                }

                return CopyRequestedRegion(
                    requestedScreenBounds,
                    clientBounds,
                    windowBounds);
            }
        }

        private void Initialize(IntPtr windowHandle)
        {
            try
            {
                _device = new Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
                _context = _device.ImmediateContext;
                _winRtDevice = CreateWinRtDevice(_device);
                _item = CreateCaptureItem(windowHandle);
                _poolSize = _item.Size;
                if (_poolSize.Width <= 0 || _poolSize.Height <= 0)
                {
                    throw new InvalidOperationException("The game window has no capturable content yet.");
                }

                _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _winRtDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    _poolSize);
                _item.Closed += OnCaptureClosed;
                _session = _framePool.CreateCaptureSession(_item);
                _session.StartCapture();
            }
            catch
            {
                DisposeResources();
                throw;
            }
        }

        private void DrainAvailableFrames()
        {
            bool recreatePool = false;
            for (int i = 0; i < MaxFramesToDrainPerRequest; i++)
            {
                Direct3D11CaptureFrame frame = _framePool.TryGetNextFrame();
                if (frame == null)
                {
                    break;
                }

                using (frame)
                using (Texture2D frameTexture = CreateSharpDxTexture2D(frame.Surface))
                {
                    UpdateLatestTexture(frameTexture);
                    SizeInt32 contentSize = frame.ContentSize;
                    if (contentSize.Width > 0 && contentSize.Height > 0 &&
                        (contentSize.Width != _poolSize.Width || contentSize.Height != _poolSize.Height))
                    {
                        _poolSize = contentSize;
                        recreatePool = true;
                    }
                }

                if (recreatePool)
                {
                    break;
                }
            }

            if (recreatePool)
            {
                _framePool.Recreate(
                    _winRtDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    _poolSize);
            }
        }

        private void UpdateLatestTexture(Texture2D frameTexture)
        {
            Texture2DDescription sourceDescription = frameTexture.Description;
            if (_latestTexture == null ||
                _latestTexture.Description.Width != sourceDescription.Width ||
                _latestTexture.Description.Height != sourceDescription.Height ||
                _latestTexture.Description.Format != sourceDescription.Format)
            {
                _latestTexture?.Dispose();
                _latestTexture = new Texture2D(_device, new Texture2DDescription
                {
                    Width = sourceDescription.Width,
                    Height = sourceDescription.Height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = sourceDescription.Format,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.None,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                });
            }

            _context.CopyResource(frameTexture, _latestTexture);
        }

        private Bitmap CopyRequestedRegion(
            Rectangle requestedScreenBounds,
            Rectangle clientBounds,
            Rectangle windowBounds)
        {
            int frameWidth = _latestTexture.Description.Width;
            int frameHeight = _latestTexture.Description.Height;
            Rectangle frameScreenBounds = SelectFrameScreenBounds(
                frameWidth,
                frameHeight,
                clientBounds,
                windowBounds);
            Rectangle clippedScreenBounds = Rectangle.Intersect(
                requestedScreenBounds,
                frameScreenBounds);
            if (clippedScreenBounds.Width <= 0 || clippedScreenBounds.Height <= 0)
            {
                return null;
            }

            double scaleX = frameWidth / (double)frameScreenBounds.Width;
            double scaleY = frameHeight / (double)frameScreenBounds.Height;
            int left = Clamp(
                (int)Math.Floor((clippedScreenBounds.Left - frameScreenBounds.Left) * scaleX),
                0,
                frameWidth);
            int top = Clamp(
                (int)Math.Floor((clippedScreenBounds.Top - frameScreenBounds.Top) * scaleY),
                0,
                frameHeight);
            int right = Clamp(
                (int)Math.Ceiling((clippedScreenBounds.Right - frameScreenBounds.Left) * scaleX),
                0,
                frameWidth);
            int bottom = Clamp(
                (int)Math.Ceiling((clippedScreenBounds.Bottom - frameScreenBounds.Top) * scaleY),
                0,
                frameHeight);
            if (right <= left || bottom <= top)
            {
                return null;
            }

            Bitmap capturedCrop = CopyTextureRegion(left, top, right - left, bottom - top);
            if (clippedScreenBounds == requestedScreenBounds &&
                capturedCrop.Width == requestedScreenBounds.Width &&
                capturedCrop.Height == requestedScreenBounds.Height)
            {
                return capturedCrop;
            }

            if (clippedScreenBounds != requestedScreenBounds)
            {
                capturedCrop.Dispose();
                return null;
            }

            Bitmap normalized = null;
            try
            {
                normalized = new Bitmap(
                    requestedScreenBounds.Width,
                    requestedScreenBounds.Height,
                    PixelFormat.Format24bppRgb);
                using (Graphics graphics = Graphics.FromImage(normalized))
                {
                    graphics.DrawImage(
                        capturedCrop,
                        new Rectangle(0, 0, normalized.Width, normalized.Height));
                }
                return normalized;
            }
            catch
            {
                normalized?.Dispose();
                throw;
            }
            finally
            {
                capturedCrop.Dispose();
            }
        }

        private static Rectangle SelectFrameScreenBounds(
            int frameWidth,
            int frameHeight,
            Rectangle clientBounds,
            Rectangle windowBounds)
        {
            if (windowBounds.Width <= 0 || windowBounds.Height <= 0)
            {
                return clientBounds;
            }

            long clientError = Math.Abs(frameWidth - clientBounds.Width)
                + Math.Abs(frameHeight - clientBounds.Height);
            long windowError = Math.Abs(frameWidth - windowBounds.Width)
                + Math.Abs(frameHeight - windowBounds.Height);
            return windowError < clientError ? windowBounds : clientBounds;
        }

        private Bitmap CopyTextureRegion(int left, int top, int width, int height)
        {
            var stagingDescription = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = _latestTexture.Description.Format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CpuAccessFlags = CpuAccessFlags.Read,
                OptionFlags = ResourceOptionFlags.None
            };

            using (var staging = new Texture2D(_device, stagingDescription))
            {
                _context.CopySubresourceRegion(
                    _latestTexture,
                    0,
                    new ResourceRegion
                    {
                        Left = left,
                        Top = top,
                        Front = 0,
                        Right = left + width,
                        Bottom = top + height,
                        Back = 1
                    },
                    staging,
                    0,
                    0,
                    0,
                    0);

                SharpDX.DataBox mapped = _context.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
                try
                {
                    return CreateBgrBitmap(mapped, width, height);
                }
                finally
                {
                    _context.UnmapSubresource(staging, 0);
                }
            }
        }

        private static Bitmap CreateBgrBitmap(SharpDX.DataBox mapped, int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            BitmapData bitmapData = null;
            bool succeeded = false;
            try
            {
                bitmapData = bitmap.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format24bppRgb);
                var sourceRow = new byte[width * 4];
                var destinationRow = new byte[width * 3];
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(
                        IntPtr.Add(mapped.DataPointer, y * mapped.RowPitch),
                        sourceRow,
                        0,
                        sourceRow.Length);
                    for (int x = 0; x < width; x++)
                    {
                        int sourceIndex = x * 4;
                        int destinationIndex = x * 3;
                        destinationRow[destinationIndex] = sourceRow[sourceIndex];
                        destinationRow[destinationIndex + 1] = sourceRow[sourceIndex + 1];
                        destinationRow[destinationIndex + 2] = sourceRow[sourceIndex + 2];
                    }

                    Marshal.Copy(
                        destinationRow,
                        0,
                        IntPtr.Add(bitmapData.Scan0, y * bitmapData.Stride),
                        destinationRow.Length);
                }

                succeeded = true;
                return bitmap;
            }
            finally
            {
                if (bitmapData != null)
                {
                    bitmap.UnlockBits(bitmapData);
                }
                if (!succeeded)
                {
                    bitmap.Dispose();
                }
            }
        }

        private static GraphicsCaptureItem CreateCaptureItem(IntPtr windowHandle)
        {
            var factory = WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
            var interop = (IGraphicsCaptureItemInterop)factory;
            Guid itemGuid = GraphicsCaptureItemGuid;
            IntPtr itemPointer = interop.CreateForWindow(windowHandle, ref itemGuid);
            if (itemPointer == IntPtr.Zero)
            {
                throw new InvalidOperationException("Windows Graphics Capture returned an empty capture item.");
            }

            try
            {
                return Marshal.GetObjectForIUnknown(itemPointer) as GraphicsCaptureItem
                    ?? throw new InvalidOperationException("Could not create a Windows Graphics Capture item for the game window.");
            }
            finally
            {
                Marshal.Release(itemPointer);
            }
        }

        private static IDirect3DDevice CreateWinRtDevice(Device device)
        {
            using (var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device3>())
            {
                int hresult = CreateDirect3D11DeviceFromDXGIDevice(
                    dxgiDevice.NativePointer,
                    out IntPtr inspectableDevice);
                Marshal.ThrowExceptionForHR(hresult);
                try
                {
                    return Marshal.GetObjectForIUnknown(inspectableDevice) as IDirect3DDevice
                        ?? throw new InvalidOperationException("Could not create a WinRT Direct3D device.");
                }
                finally
                {
                    Marshal.Release(inspectableDevice);
                }
            }
        }

        private static Texture2D CreateSharpDxTexture2D(IDirect3DSurface surface)
        {
            var access = (IDirect3DDxgiInterfaceAccess)surface;
            Guid textureGuid = Id3D11Texture2DGuid;
            IntPtr texturePointer = access.GetInterface(ref textureGuid);
            if (texturePointer == IntPtr.Zero)
            {
                throw new InvalidOperationException("The captured frame did not expose a Direct3D texture.");
            }

            return new Texture2D(texturePointer);
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private void OnCaptureClosed(GraphicsCaptureItem sender, object args)
        {
            _captureClosed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GameWindowCaptureSource));
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                DisposeResources();
            }
        }

        private void DisposeResources()
        {
            if (_item != null)
            {
                _item.Closed -= OnCaptureClosed;
            }
            _session?.Dispose();
            _session = null;
            _framePool?.Dispose();
            _framePool = null;
            _latestTexture?.Dispose();
            _latestTexture = null;
            _context?.Dispose();
            _context = null;
            _device?.Dispose();
            _device = null;
            _winRtDevice = null;
            _item = null;
        }

        [DllImport("ntdll.dll")]
        private static extern int RtlGetVersion(ref NativeOsVersionInfo versionInfo);

        [DllImport(
            "d3d11.dll",
            EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice",
            ExactSpelling = true,
            CallingConvention = CallingConvention.StdCall)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(
            IntPtr dxgiDevice,
            out IntPtr graphicsDevice);

        [ComImport]
        [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [ComVisible(true)]
        private interface IDirect3DDxgiInterfaceAccess
        {
            IntPtr GetInterface([In] ref Guid iid);
        }

        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [ComVisible(true)]
        private interface IGraphicsCaptureItemInterop
        {
            IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);

            IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeOsVersionInfo
        {
            public int Size;
            public int MajorVersion;
            public int MinorVersion;
            public int BuildNumber;
            public int PlatformId;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string ServicePack;
        }
    }
}
