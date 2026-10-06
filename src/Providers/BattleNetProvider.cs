using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;

namespace GhostMode
{
    /// <summary>
    /// Battle.net's embedded browser exposes no accessibility tree, so this clicks at offsets measured from the
    /// top-right of its client area (96-DPI units) and reads the presence dot on the avatar to confirm.
    /// Re-measure these if Blizzard moves things around.
    /// </summary>
    public class BattleNetProvider : ProviderBase
    {
        static readonly int[] ProfileButton = { 196, 47 };
        static readonly int[] OnlineItem = { 266, 178 };
        static readonly int[] OfflineItem = { 240, 274 };
        static readonly int[] MenuOnlineDot = { 305, 178 };
        static readonly int[] StatusDot = { 260, 74 };   // on the ring: offline is a hollow grey ring, online a filled green dot

        static readonly string Exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Battle.net\Battle.net.exe");

        public override string Id { get { return "battlenet"; } }
        public override string DisplayName { get { return "Battle.net"; } }
        public override string Caveat { get { return "Uses screen positions; may need updating after a Battle.net redesign"; } }

        public override ImageSource LoadIcon() { return Icons.FromExe(Exe); }

        public override Availability Detect()
        {
            if (Native.IsRunning("Battle.net")) return Availability.Running;
            return File.Exists(Exe) ? Availability.NotRunning : Availability.NotInstalled;
        }

        static Native.WindowInfo MainWindow()
        {
            return Native.FindWindows("Battle.net", "^Chrome_WidgetWin_0$", "^Battle\\.net$").FirstOrDefault();
        }

        /// <summary>The menu's "Online" entry has a green dot; if it's there, the menu is open.</summary>
        static bool MenuOpen(IntPtr hwnd)
        {
            var c = SnapshotPixel(hwnd, MenuOnlineDot[0], MenuOnlineDot[1], true);
            return c.HasValue && ClassifyDot(c.Value) == Presence.Online;
        }

        static Presence? ReadDot(IntPtr hwnd)
        {
            var c = SnapshotPixel(hwnd, StatusDot[0], StatusDot[1], true);
            if (!c.HasValue) return null;
            var p = ClassifyDot(c.Value);
            return p == Presence.Unknown ? (Presence?)null : p;
        }

        public override Presence? ReadLive()
        {
            var w = MainWindow();
            return w == null ? null : ReadDot(w.Handle);
        }

        static WindowSession Session()
        {
            // Battle.net's close button sends it to the tray; hiding it directly leaves the tray icon unable to reopen it
            return new WindowSession(MainWindow, () => Process.Start(Exe), TrayReturn.Close, 15000);
        }

        public override Presence? Probe()
        {
            using (var s = Session()) { var h = s.Handle; return WaitForDot(() => ReadDot(h), 6000); }
        }

        public override SetResult Set(bool invisible)
        {
            using (var s = Session())
            {
                var h = s.Handle;
                var before = WaitForDot(() => ReadDot(h), 6000);
                if (!before.HasValue) throw new InvalidOperationException("couldn't read the status dot; the layout may have moved");
                var target = invisible ? Presence.Invisible : Presence.Online;
                if (before == target) return new SetResult(target, "already " + (invisible ? "appear offline" : "online"));

                // Right after coming back from the tray the page ignores clicks for a moment, so retry until the menu shows
                bool open = false;
                for (int attempt = 0; attempt < 4 && !open; attempt++)
                {
                    var b = ClientPoint(h, ProfileButton[0], ProfileButton[1], true);
                    Native.Click(b.X, b.Y);
                    open = Native.WaitUntil(() => MenuOpen(h), 1200, 150);
                }
                if (!open) throw new InvalidOperationException("status menu didn't open; the layout may have moved");
                var item = invisible ? OfflineItem : OnlineItem;
                var p = ClientPoint(h, item[0], item[1], true);
                Native.Click(p.X, p.Y);

                Presence? after = null;
                if (Native.WaitUntil(() => { after = ReadDot(h); return after == target; }, 4000))
                    return new SetResult(target, "set " + (invisible ? "appear offline" : "online"));
                Native.PressEscape();
                throw new InvalidOperationException("status dot didn't change; the layout may have moved");
            }
        }
    }
}
