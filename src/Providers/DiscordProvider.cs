using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Automation;
using System.Windows.Media;

namespace GhostMode
{
    /// <summary>
    /// Discord's Electron UI exposes a full accessibility tree. The user panel's status text can be read even while
    /// the window is in the tray, but the status menu only renders while the window is visible.
    /// </summary>
    public class DiscordProvider : ProviderBase
    {
        static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");
        static readonly string UpdateExe = Path.Combine(Root, "Update.exe");

        public override string Id { get { return "discord"; } }
        public override string DisplayName { get { return "Discord"; } }

        public override ImageSource LoadIcon()
        {
            return Icons.FromFile(Path.Combine(Root, "app.ico")) ?? Icons.FromExe(UpdateExe);
        }

        public override Availability Detect()
        {
            if (Native.IsRunning("Discord")) return Availability.Running;
            return File.Exists(UpdateExe) ? Availability.NotRunning : Availability.NotInstalled;
        }

        static Native.WindowInfo MainWindow()
        {
            return Native.FindWindows("Discord", "^Chrome_WidgetWin_1$", "Discord$").FirstOrDefault();
        }

        public override Presence? ReadLive()
        {
            var w = MainWindow();
            if (w == null) return null;
            var root = AutomationElement.FromHandle(w.Handle);
            var panel = FindByName(root, "User status and settings");
            if (panel == null) return null;
            // The line under the username shows the status, unless a custom status is set (then we can't tell from here)
            foreach (AutomationElement t in panel.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)))
            {
                var p = Parse(t.Current.Name);
                if (p != Presence.Unknown) return p;
            }
            return null;
        }

        static Presence Parse(string s)
        {
            if (s == null) return Presence.Unknown;
            s = s.Trim();
            if (s.StartsWith("Online")) return Presence.Online;
            if (s.StartsWith("Idle")) return Presence.Away;
            if (s.StartsWith("Do Not Disturb")) return Presence.Busy;
            if (s.StartsWith("Invisible")) return Presence.Invisible;
            return Presence.Unknown;
        }

        public override SetResult Set(bool invisible)
        {
            string want = invisible ? "invisible" : "online";
            using (var session = new WindowSession(MainWindow, () => Process.Start(UpdateExe, "--processStart Discord.exe"), TrayReturn.Hide, 10000))
            {
                var root = AutomationElement.FromHandle(session.Handle);
                var button = Native.WaitFor(() => FindByName(root, "Manage profile and status"), 10000);
                if (button == null) throw new InvalidOperationException("couldn't find the profile button");
                var ec = (ExpandCollapsePattern)button.GetCurrentPattern(ExpandCollapsePattern.Pattern);
                try
                {
                    if (ec.Current.ExpandCollapseState != ExpandCollapseState.Expanded) ec.Expand();
                    var row = Native.WaitFor(() => FindByNameRegex(root, "^Your Status: "), 4000);
                    if (row == null) throw new InvalidOperationException("profile popout didn't open");
                    var before = Parse(row.Current.Name.Substring("Your Status: ".Length));
                    if (before == (invisible ? Presence.Invisible : Presence.Online))
                        return new SetResult(before, "already " + want);

                    ((ExpandCollapsePattern)row.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
                    var item = Native.WaitFor(() => FindById(root, "set-status-submenu-" + want), 4000);
                    if (item == null) throw new InvalidOperationException("status menu didn't open");
                    Activate(item);
                }
                finally
                {
                    try { if (ec.Current.ExpandCollapseState == ExpandCollapseState.Expanded) ec.Collapse(); } catch { }
                }

                var target = invisible ? Presence.Invisible : Presence.Online;
                Presence? after = null;
                Native.WaitUntil(() => { after = ReadLive(); return after == target; }, 3000);
                if (after.HasValue && after.Value != target) throw new InvalidOperationException("status still shows " + after.Value);
                return new SetResult(target, "set " + want);
            }
        }
    }
}
