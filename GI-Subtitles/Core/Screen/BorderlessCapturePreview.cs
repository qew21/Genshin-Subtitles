using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Security.Authorization.AppCapabilityAccess;

namespace GI_Subtitles.Core.Screen
{
    internal sealed class BorderlessCapturePreview : IDisposable
    {
        private const int AppModelErrorNoPackage = 15700;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFamilyName(ref uint length, IntPtr name);

        internal static bool BootstrapIfUnpackaged(string appDirectory)
        {
            uint length = 0;
            if (GetCurrentPackageFamilyName(ref length, IntPtr.Zero) != AppModelErrorNoPackage)
            {
                return false;
            }

            try
            {
                string script = Path.Combine(appDirectory, "Launch-BorderlessCapturePreview.ps1");
                if (!File.Exists(script))
                {
                    throw new FileNotFoundException("测试版缺少启动脚本，请重新准备测试目录。", script);
                }

                using (var current = Process.GetCurrentProcess())
                using (Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -BootstrapProcessId {current.Id}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }))
                {
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "GI-Subtitles 无边框采集测试");
            }

            return true;
        }

        private readonly object _sync = new object();
        private volatile bool _authorized;
        private IntPtr _window;
        private GameWindowCaptureSource _source;
        private bool _disposed;

        internal async Task AuthorizeAsync()
        {
            if (!ApiInformation.IsPropertyPresent(
                "Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
            {
                throw new PlatformNotSupportedException("此 Windows 版本不支持无边框窗口采集。");
            }

            var status = await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
            if (status != AppCapabilityAccessStatus.Allowed)
            {
                throw new InvalidOperationException($"Windows 未允许无边框采集（{status}）。请从开始菜单启动测试版，并检查系统隐私设置中的截图边框权限。");
            }

            _authorized = true;
        }

        internal Bitmap Capture(string game, Rectangle bounds)
        {
            lock (_sync)
            {
                if (_disposed || !_authorized)
                {
                    throw new InvalidOperationException("无边框窗口采集尚未就绪。");
                }

                if (!GameWindowLocator.TryFind(game, out GameWindowInfo window))
                {
                    ResetSource();
                    throw new InvalidOperationException($"找不到游戏窗口（{game}）。请先打开游戏。");
                }

                if (_source == null || _window != window.Handle)
                {
                    ResetSource();
                    _source = GameWindowCaptureSource.Create(window.Handle);
                    _window = window.Handle;
                }

                return _source.Capture(bounds, window.ClientBounds, window.WindowBounds)
                    ?? throw new InvalidOperationException($"游戏窗口暂未提供有效画面，或识别区超出游戏窗口（{bounds}）。");
            }
        }

        private void ResetSource()
        {
            _source?.Dispose();
            _source = null;
            _window = IntPtr.Zero;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                ResetSource();
            }
        }
    }
}
