using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace GI_Subtitles.Core.Screen
{
    public sealed class GameWindowInfo
    {
        internal GameWindowInfo(
            IntPtr handle,
            string processName,
            string title,
            Rectangle clientBounds,
            Rectangle windowBounds,
            bool fullScreen)
        {
            Handle = handle;
            ProcessName = processName ?? string.Empty;
            Title = title ?? string.Empty;
            ClientBounds = clientBounds;
            WindowBounds = windowBounds;
            IsFullScreen = fullScreen;
        }

        public IntPtr Handle { get; }

        public string ProcessName { get; }

        public string Title { get; }

        public Rectangle ClientBounds { get; }

        public Rectangle WindowBounds { get; }

        public bool IsFullScreen { get; }
    }

    /// <summary>
    /// Finds the selected game's visible top-level window using window metadata only.
    /// This does not require opening the game's process with elevated permissions.
    /// </summary>
    public static class GameWindowLocator
    {
        private sealed class Candidate
        {
            public IntPtr Handle;
            public string ProcessName;
            public string Title;
            public Rectangle ClientBounds;
            public Rectangle WindowBounds;
            public bool FullScreen;
            public int Score;
        }

        private static readonly Dictionary<string, string[]> ProcessAliases =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "Genshin", new[] { "YuanShen", "GenshinImpact" } },
                { "StarRail", new[] { "StarRail", "HonkaiStarRail" } },
                { "Zenless", new[] { "ZenlessZoneZero" } },
                { "Wuthering", new[] { "WutheringWaves", "Client-Win64-Shipping" } },
                { "Endfield", new[] { "Endfield", "ArknightsEndfield" } },
                { "BH3", new[] { "BH3", "HonkaiImpact3" } }
            };

        private static readonly Dictionary<string, string[]> TitleAliases =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "Genshin", new[] { "Genshin Impact", "原神" } },
                { "StarRail", new[] { "Honkai Star Rail", "Star Rail", "星穹铁道", "崩坏：星穹铁道" } },
                { "Zenless", new[] { "Zenless Zone Zero", "绝区零" } },
                { "Wuthering", new[] { "Wuthering Waves", "鸣潮" } },
                { "Endfield", new[] { "Arknights Endfield", "明日方舟：终末地" } },
                { "BH3", new[] { "Honkai Impact 3rd", "崩坏3", "崩壊3rd" } }
            };

        public static bool TryFind(string game, out GameWindowInfo window)
        {
            window = null;
            if (string.IsNullOrWhiteSpace(game))
            {
                return false;
            }

            ProcessAliases.TryGetValue(game, out string[] processAliases);
            TitleAliases.TryGetValue(game, out string[] titleAliases);
            if ((processAliases == null || processAliases.Length == 0) &&
                (titleAliases == null || titleAliases.Length == 0))
            {
                return false;
            }

            IntPtr foreground = GetForegroundWindow();
            int currentProcessId;
            using (Process currentProcess = Process.GetCurrentProcess())
            {
                currentProcessId = currentProcess.Id;
            }

            var candidates = new List<Candidate>();
            var processNames = new Dictionary<uint, string>();

            EnumWindows((handle, state) =>
            {
                if (handle == IntPtr.Zero || !IsWindowVisible(handle) || IsIconic(handle))
                {
                    return true;
                }

                GetWindowThreadProcessId(handle, out uint processId);
                if (processId == 0 || processId == currentProcessId)
                {
                    return true;
                }

                string title = GetWindowTitle(handle);
                if (!processNames.TryGetValue(processId, out string processName))
                {
                    processName = GetProcessName(processId);
                    processNames[processId] = processName;
                }

                bool processMatches = ContainsIgnoreCase(processAliases, processName);
                bool titleMatches = ContainsAnyNormalized(title, titleAliases);
                if (!processMatches && !titleMatches)
                {
                    return true;
                }

                Rectangle clientBounds;
                if (!TryGetClientBounds(handle, out clientBounds) ||
                    clientBounds.Width < 320 || clientBounds.Height < 240)
                {
                    return true;
                }

                if (!TryGetWindowBounds(handle, out Rectangle windowBounds))
                {
                    windowBounds = clientBounds;
                }

                bool fullScreen = CoversMonitor(clientBounds);
                int score = (processMatches ? 10000 : 0)
                    + (titleMatches ? 1000 : 0)
                    + (handle == foreground ? 500 : 0)
                    + (fullScreen ? 250 : 0)
                    + Math.Min(200, (clientBounds.Width * clientBounds.Height) / 100000);

                candidates.Add(new Candidate
                {
                    Handle = handle,
                    ProcessName = processName,
                    Title = title,
                    ClientBounds = clientBounds,
                    WindowBounds = windowBounds,
                    FullScreen = fullScreen,
                    Score = score
                });
                return true;
            }, IntPtr.Zero);

            Candidate best = candidates
                .OrderByDescending(candidate => candidate.Score)
                .FirstOrDefault();
            if (best == null)
            {
                return false;
            }

            window = new GameWindowInfo(
                best.Handle,
                best.ProcessName,
                best.Title,
                best.ClientBounds,
                best.WindowBounds,
                best.FullScreen);
            return true;
        }

        private static string GetProcessName(uint processId)
        {
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    return process.ProcessName;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetWindowTitle(IntPtr handle)
        {
            int length = GetWindowTextLength(handle);
            if (length <= 0)
            {
                return string.Empty;
            }

            var title = new StringBuilder(length + 1);
            GetWindowText(handle, title, title.Capacity);
            return title.ToString();
        }

        private static bool ContainsIgnoreCase(string[] values, string value)
        {
            return values != null && values.Any(candidate =>
                string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ContainsAnyNormalized(string title, string[] aliases)
        {
            if (string.IsNullOrWhiteSpace(title) || aliases == null)
            {
                return false;
            }

            string normalizedTitle = Normalize(title);
            return aliases.Any(alias =>
            {
                string normalizedAlias = Normalize(alias);
                return normalizedAlias.Length > 0 && normalizedTitle.Contains(normalizedAlias);
            });
        }

        private static string Normalize(string value)
        {
            var normalized = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character))
                {
                    normalized.Append(char.ToLowerInvariant(character));
                }
            }

            return normalized.ToString();
        }

        private static bool TryGetClientBounds(IntPtr handle, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (!GetClientRect(handle, out NativeRect client))
            {
                return false;
            }

            var topLeft = new NativePoint { X = client.Left, Y = client.Top };
            var bottomRight = new NativePoint { X = client.Right, Y = client.Bottom };
            if (!ClientToScreen(handle, ref topLeft) || !ClientToScreen(handle, ref bottomRight))
            {
                return false;
            }

            bounds = Rectangle.FromLTRB(
                topLeft.X,
                topLeft.Y,
                bottomRight.X,
                bottomRight.Y);
            return bounds.Width > 0 && bounds.Height > 0;
        }

        private static bool TryGetWindowBounds(IntPtr handle, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (!GetWindowRect(handle, out NativeRect rect))
            {
                return false;
            }

            bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return bounds.Width > 0 && bounds.Height > 0;
        }

        private static bool CoversMonitor(Rectangle clientBounds)
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle monitor = screen.Bounds;
                if (Math.Abs(clientBounds.Left - monitor.Left) <= 4 &&
                    Math.Abs(clientBounds.Top - monitor.Top) <= 4 &&
                    Math.Abs(clientBounds.Right - monitor.Right) <= 4 &&
                    Math.Abs(clientBounds.Bottom - monitor.Bottom) <= 4)
                {
                    return true;
                }
            }

            return false;
        }

        private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr state);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr handle, StringBuilder title, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(IntPtr handle, out NativeRect rect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ClientToScreen(IntPtr handle, ref NativePoint point);
    }
}
