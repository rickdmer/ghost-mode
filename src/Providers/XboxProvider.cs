using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Media;
using Microsoft.Win32;

namespace GhostMode
{
    /// <summary>
    /// The Xbox PC app (WinUI) exposes a good accessibility tree, but its status lives behind the profile menu: the item reads
    /// "Appear offline" while online and "Appear online" while offline. Its items only respond to real clicks.
    /// </summary>
    public class XboxProvider : ProviderBase
    {
        const string AppUserModelId = @"shell:AppsFolder\Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App";

        public override string Id { get { return "xbox"; } }
        public override string DisplayName { get { return "Xbox"; } }

        static string PackageRoot()
        {
            const string repo = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
            using (var k = Registry.CurrentUser.OpenSubKey(repo))
            {
                if (k == null) return null;
                var name = k.GetSubKeyNames().Where(n => n.StartsWith("Microsoft.GamingApp_") && n.Contains("_x64_")).OrderByDescending(n => n).FirstOrDefault();
                if (name == null) return null;
                using (var p = k.OpenSubKey(name)) return p == null ? null : p.GetValue("PackageRootFolder") as string;
            }
        }

        public override ImageSource LoadIcon()
        {
            var root = PackageRoot();
            if (root == null) return null;
            foreach (var f in new[] { "Xbox_AppList.scale-200.png", "Xbox_AppList.targetsize-48.png", "StoreLogo.png" })
            {
                var img = Icons.FromFile(Path.Combine(root, "Assets", f));
                if (img != null) return img;
            }
            return null;
        }

        public override Availability Detect()
        {
            if (Native.IsRunning("XboxPcApp", "XboxPcTray")) return Availability.Running;
            return PackageRoot() != null ? Availability.NotRunning : Availability.NotInstalled;
        }

        /// <summary>The visible XBOX frame. Closed UWP frames linger as cloaked windows; UIA's top-level list skips those.</summary>
        static Native.WindowInfo MainWindow()
        {
            foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ClassNameProperty, "ApplicationFrameWindow")))
            {
                if (w.Current.Name == "XBOX")
                    return new Native.WindowInfo { Handle = new IntPtr(w.Current.NativeWindowHandle), Title = "XBOX" };
            }
            return null;
        }

        const string ItemPattern = "^Appear (offline|online)$";

        /// <summary>Opens the profile menu (if needed) and returns the Appear online/offline item.</summary>
        static AutomationElement OpenMenu(AutomationElement root, AutomationElement button)
        {
            var item = FindByNameRegex(root, ItemPattern);
            if (item != null) return item;
            ClickElement(button);
            return Native.WaitFor(() => FindByNameRegex(root, ItemPattern), 4000);
        }

        static WindowSession Session()
        {
            return new WindowSession(MainWindow, () => Process.Start(AppUserModelId), TrayReturn.Close, 15000);
        }

        static AutomationElement ProfileButton(AutomationElement root)
        {
            var b = Native.WaitFor(() => FindById(root, "ProfileSettingsButton_MainButton"), 15000);
            if (b == null) throw new InvalidOperationException("couldn't find the profile button");
            return b;
        }

        public override Presence? Probe()
        {
            using (var session = Session())
            {
                var root = AutomationElement.FromHandle(session.Handle);
                var button = ProfileButton(root);
                var item = OpenMenu(root, button);
                if (item == null) return null;
                var label = item.Current.Name;
                ClickElement(button);
                return label == "Appear online" ? Presence.Invisible : Presence.Online;
            }
        }

        public override SetResult Set(bool invisible)
        {
            using (var session = Session())
            {
                var root = AutomationElement.FromHandle(session.Handle);
                var button = ProfileButton(root);
                var item = OpenMenu(root, button);
                if (item == null) throw new InvalidOperationException("profile menu didn't open");
                var label = item.Current.Name;
                bool isOffline = label == "Appear online";
                var target = invisible ? Presence.Invisible : Presence.Online;
                if (isOffline == invisible)
                {
                    ClickElement(button); // close the menu
                    return new SetResult(target, "already " + (invisible ? "offline" : "online"));
                }
                ClickElement(item);

                // The change round-trips to Xbox Live; reopen the menu until the label flips
                for (int i = 0; i < 4; i++)
                {
                    Thread.Sleep(1200);
                    var check = OpenMenu(root, button);
                    string now = check == null ? null : check.Current.Name;
                    if (check != null) ClickElement(button);
                    if (now != null && now != label) return new SetResult(target, "set " + (invisible ? "appear offline" : "online"));
                }
                throw new InvalidOperationException("menu still says '" + label + "'");
            }
        }
    }
}
