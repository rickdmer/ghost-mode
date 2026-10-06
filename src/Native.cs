using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace GhostMode
{
    /// <summary>Win32 plumbing shared by the providers: finding windows, showing/hiding them, clicking, and reading pixels.</summary>
    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref POINT p);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern void mouse_event(uint flags, int x, int y, uint data, UIntPtr extra);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4, SW_MINIMIZE = 6, SW_RESTORE = 9;
        public const uint WM_CLOSE = 0x10;
        public static readonly IntPtr DPI_PER_MONITOR_V2 = new IntPtr(-4);

        public class WindowInfo
        {
            public IntPtr Handle; public string ClassName; public string Title; public int ProcessId; public string ProcessName;
            public bool Visible { get { return IsWindowVisible(Handle); } }
            public bool Minimized { get { return IsIconic(Handle); } }
        }

        /// <summary>Top-level windows (including hidden ones) owned by processes with the given name.</summary>
        public static List<WindowInfo> FindWindows(string processName, string classRegex, string titleRegex)
        {
            var pids = new Dictionary<int, string>();
            foreach (var p in Process.GetProcessesByName(processName)) { pids[p.Id] = p.ProcessName; p.Dispose(); }
            var result = new List<WindowInfo>();
            if (pids.Count == 0) return result;
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                uint pid; GetWindowThreadProcessId(h, out pid);
                if (!pids.ContainsKey((int)pid)) return true;
                var c = new StringBuilder(256); GetClassName(h, c, 256);
                var t = new StringBuilder(512); GetWindowText(h, t, 512);
                if (Regex.IsMatch(c.ToString(), classRegex) && Regex.IsMatch(t.ToString(), titleRegex))
                    result.Add(new WindowInfo { Handle = h, ClassName = c.ToString(), Title = t.ToString(), ProcessId = (int)pid, ProcessName = pids[(int)pid] });
                return true;
            }, IntPtr.Zero);
            return result;
        }

        public static bool IsRunning(params string[] processNames)
        {
            foreach (var n in processNames)
            {
                var ps = Process.GetProcessesByName(n);
                bool any = ps.Length > 0;
                foreach (var p in ps) p.Dispose();
                if (any) return true;
            }
            return false;
        }

        public static double Scale(IntPtr hwnd)
        {
            uint dpi = GetDpiForWindow(hwnd);
            return dpi == 0 ? 1.0 : dpi / 96.0;
        }

        /// <summary>Client area in screen pixels (per-monitor-aware thread).</summary>
        public static Rectangle ClientRect(IntPtr hwnd)
        {
            RECT r; GetClientRect(hwnd, out r);
            var p = new POINT(); ClientToScreen(hwnd, ref p);
            return new Rectangle(p.X, p.Y, r.Right - r.Left, r.Bottom - r.Top);
        }

        /// <summary>Alt tap so SetForegroundWindow is honoured even though we aren't the foreground app.</summary>
        public static void Focus(IntPtr hwnd)
        {
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            SetForegroundWindow(hwnd);
        }

        /// <summary>Left-click at a screen point, then put the cursor back where it was.</summary>
        public static void Click(int x, int y)
        {
            POINT old; GetCursorPos(out old);
            SetCursorPos(x, y); Thread.Sleep(60);
            mouse_event(0x02, 0, 0, 0, UIntPtr.Zero);
            mouse_event(0x04, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(60);
            SetCursorPos(old.X, old.Y);
        }

        public static void PressEscape()
        {
            keybd_event(0x1B, 0, 0, UIntPtr.Zero);
            keybd_event(0x1B, 0, 2, UIntPtr.Zero);
        }

        public static Color ScreenPixel(int x, int y)
        {
            using (var bmp = new Bitmap(1, 1))
            {
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
                return bmp.GetPixel(0, 0);
            }
        }

        /// <summary>Snapshot of the client area even when it's covered by other windows (not when hidden/minimised).</summary>
        public static Bitmap SnapshotClient(IntPtr hwnd)
        {
            RECT wr; GetWindowRectNative(hwnd, out wr);
            var client = ClientRect(hwnd);
            if (wr.Right - wr.Left <= 0 || wr.Bottom - wr.Top <= 0) return null;
            using (var full = new Bitmap(wr.Right - wr.Left, wr.Bottom - wr.Top))
            {
                using (var g = Graphics.FromImage(full))
                {
                    var hdc = g.GetHdc();
                    PrintWindow(hwnd, hdc, 2 /* PW_RENDERFULLCONTENT */);
                    g.ReleaseHdc(hdc);
                }
                var crop = new Rectangle(client.X - wr.Left, client.Y - wr.Top, client.Width, client.Height);
                crop.Intersect(new Rectangle(0, 0, full.Width, full.Height));
                if (crop.Width <= 0 || crop.Height <= 0) return null;
                return full.Clone(crop, full.PixelFormat);
            }
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowRect")] static extern bool GetWindowRectNative(IntPtr hwnd, out RECT r);

        public static T WaitFor<T>(Func<T> probe, int timeoutMs, int pollMs = 200) where T : class
        {
            var sw = Stopwatch.StartNew();
            do
            {
                try { var r = probe(); if (r != null) return r; } catch { }
                Thread.Sleep(pollMs);
            } while (sw.ElapsedMilliseconds < timeoutMs);
            return null;
        }

        public static bool WaitUntil(Func<bool> probe, int timeoutMs, int pollMs = 200)
        {
            var sw = Stopwatch.StartNew();
            do
            {
                try { if (probe()) return true; } catch { }
                Thread.Sleep(pollMs);
            } while (sw.ElapsedMilliseconds < timeoutMs);
            return false;
        }
    }
}
