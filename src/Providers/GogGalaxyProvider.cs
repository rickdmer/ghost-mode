using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Media;
using Microsoft.Win32;

namespace GhostMode
{
    /// <summary>
    /// GOG Galaxy 2 renders in Qt WebEngine with no accessibility tree. The avatar menu (top-left) has Online / Invisible;
    /// this clicks at offsets from the top-left of the client area (96-DPI units) and reads the avatar's presence dot.
    /// The menu stays open after picking, so it's closed with Escape.
    /// </summary>
    public class GogGalaxyProvider : ProviderBase
    {
        static readonly int[] Avatar = { 155, 28 };
        static readonly int[] StatusDot = { 163, 40 };
        static readonly int[] OnlineItem = { 110, 126 };
        static readonly int[] InvisibleItem = { 116, 154 };
        static readonly int[] MenuProbe = { 180, 300 }; // empty spot inside the open menu: grey when open, near-black when closed

        public override string Id { get { return "gog"; } }
        public override string DisplayName { get { return "GOG Galaxy"; } }
        public override string Caveat { get { return "Uses screen positions; may need updating after a GOG Galaxy redesign"; } }

        static string Exe
        {
            get
            {
                string dir = null;
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\GalaxyClient\paths"))
                    if (k != null) dir = k.GetValue("client") as string;
                if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy");
                return Path.Combine(dir, "GalaxyClient.exe");
            }
        }

        public override ImageSource LoadIcon() { return Icons.FromExe(Exe); }

        public override Availability Detect()
        {
            if (Native.IsRunning("GalaxyClient")) return Availability.Running;
            return File.Exists(Exe) ? Availability.NotRunning : Availability.NotInstalled;
        }

        /// <summary>Galaxy recreates its main window each time it comes back from the tray, so always look it up fresh.</summary>
        static Native.WindowInfo MainWindow()
        {
            var all = Native.FindWindows("GalaxyClient", "^Qt\\d*QWindowIcon$", "^GOG GALAXY$");
            return all.FirstOrDefault(w => w.Visible) ?? all.FirstOrDefault();
        }

        /// <summary>What Galaxy's own close button does, per its settings (defaults to tray).</summary>
        static TrayReturn CloseBehaviour()
        {
            try
            {
                var cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"GOG.com\Galaxy\Configuration\config.json");
                var m = Regex.Match(File.ReadAllText(cfg), "\"whenClosingMainWindow\"\\s*:\\s*\"(\\w+)\"");
                if (m.Success && m.Groups[1].Value != "minimizeToTray") return TrayReturn.Minimize;
            }
            catch { }
            return TrayReturn.Close;
        }

        static Presence? ReadDot(IntPtr hwnd)
        {
            var c = SnapshotPixel(hwnd, StatusDot[0], StatusDot[1], false);
            if (!c.HasValue) return null;
            var p = ClassifyDot(c.Value);
            return p == Presence.Unknown ? (Presence?)null : p;
        }

        static bool MenuOpen(IntPtr hwnd)
        {
            var c = SnapshotPixel(hwnd, MenuProbe[0], MenuProbe[1], false);
            return c.HasValue && c.Value.R > 30 && c.Value.G > 30 && c.Value.B > 30;
        }

        public override Presence? ReadLive()
        {
            var w = MainWindow();
            return w == null ? null : ReadDot(w.Handle);
        }

        WindowSession Session()
        {
            // Hiding Galaxy's window directly confuses Qt (the tray icon can't bring it back), so use its own close-to-tray
            return new WindowSession(MainWindow, () => Process.Start(Exe), CloseBehaviour(), 15000);
        }

        public override Presence? Probe()
        {
            using (var s = Session()) { var h = s.Handle; return WaitForDot(() => ReadDot(h), 10000); }
        }

        public override SetResult Set(bool invisible)
        {
            using (var s = Session())
            {
                var h = s.Handle;
                var target = invisible ? Presence.Invisible : Presence.Online;
                var before = WaitForDot(() => ReadDot(h), 10000);
                if (!before.HasValue) throw new InvalidOperationException("couldn't read the status dot (" + DescribePixel(h, StatusDot[0], StatusDot[1], false) + "); the layout may have moved");
                if (before == target) return new SetResult(target, "already " + target.ToString().ToLower());

                // The menu may still be open from before; otherwise click the avatar (retrying while the page settles)
                bool open = MenuOpen(h);
                for (int attempt = 0; attempt < 4 && !open; attempt++)
                {
                    var a = ClientPoint(h, Avatar[0], Avatar[1], false);
                    Native.Click(a.X, a.Y);
                    open = Native.WaitUntil(() => MenuOpen(h), 1200, 150);
                }
                if (!open) throw new InvalidOperationException("avatar menu didn't open; the layout may have moved");
                var item = invisible ? InvisibleItem : OnlineItem;
                var p = ClientPoint(h, item[0], item[1], false);
                Native.Click(p.X, p.Y);

                Presence? after = null;
                bool ok = Native.WaitUntil(() => { after = ReadDot(h); return after == target; }, 4000);
                Native.Focus(h);
                Native.PressEscape();
                if (!ok) throw new InvalidOperationException("status dot didn't change; the layout may have moved");
                return new SetResult(target, "set " + target.ToString().ToLower());
            }
        }
    }
}
