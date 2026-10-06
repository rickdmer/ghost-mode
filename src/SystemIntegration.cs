using System;
using System.IO;
using Microsoft.Win32;

namespace GhostMode
{
    /// <summary>
    /// Optional extras the user can switch on: Start Menu / Desktop shortcuts, the hotkey shortcut, and starting with Windows.
    /// Each one's state is read from the system itself (does the shortcut / Run entry exist?), never just remembered.
    /// </summary>
    public static class SystemIntegration
    {
        public const string DefaultHotkey = "CTRL+ALT+I";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "GhostMode";
        const string Description = "Appear offline on Discord, Steam, Xbox, Battle.net and GOG Galaxy";

        public static string ExePath { get { return typeof(SystemIntegration).Assembly.Location; } }

        static string Programs { get { return Environment.GetFolderPath(Environment.SpecialFolder.Programs); } }
        public static string StartMenuShortcut { get { return Path.Combine(Programs, "Ghost Mode.lnk"); } }
        /// <summary>Explorer only honours shortcut hotkeys for shortcuts in the Start Menu or on the Desktop.</summary>
        public static string HotkeyShortcut { get { return Path.Combine(Programs, "Ghost Mode - Toggle.lnk"); } }
        public static string DesktopShortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Ghost Mode.lnk"); } }

        // ---------------------------------------------------------------- shortcuts

        static dynamic Shell() { return Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); }

        public static void CreateShortcut(string path, string arguments, string hotkey)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var lnk = Shell().CreateShortcut(path);
            lnk.TargetPath = ExePath;
            lnk.Arguments = arguments ?? "";
            lnk.WorkingDirectory = Path.GetDirectoryName(ExePath);
            lnk.IconLocation = ExePath + ",0";
            lnk.Description = Description;
            if (hotkey != null) lnk.Hotkey = hotkey;
            lnk.Save();
        }

        public static void DeleteShortcut(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>The hotkey on the toggle shortcut, formatted for display ("Ctrl+Alt+I"), or null if there isn't one.</summary>
        public static string CurrentHotkey()
        {
            if (!File.Exists(HotkeyShortcut)) return null;
            try
            {
                string raw = Shell().CreateShortcut(HotkeyShortcut).Hotkey;
                if (string.IsNullOrEmpty(raw)) return null;
                // WScript reports e.g. "Alt+Ctrl+I"; show modifiers in the usual order
                var parts = raw.Split('+');
                string key = parts[parts.Length - 1], mods = "";
                foreach (var m in new[] { "Ctrl", "Alt", "Shift" })
                    if (Array.Exists(parts, p => p.Equals(m, StringComparison.OrdinalIgnoreCase))) mods += m + "+";
                return mods + key;
            }
            catch (Exception ex) { Log.Write("couldn't read hotkey shortcut: " + ex.Message); return null; }
        }

        // ---------------------------------------------------------------- start with Windows

        public static bool StartsWithWindows
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunValue) != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(RunValue, "\"" + ExePath + "\" --tray");
                    else if (k.GetValue(RunValue) != null) k.DeleteValue(RunValue);
                }
            }
        }

        /// <summary>
        /// If the app has been moved since its shortcuts / Run entry were made, point them at where it is now.
        /// </summary>
        public static void RepairPaths()
        {
            foreach (var path in new[] { StartMenuShortcut, DesktopShortcut, HotkeyShortcut })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var lnk = Shell().CreateShortcut(path);
                    string target = lnk.TargetPath;
                    if (string.Equals(target, ExePath, StringComparison.OrdinalIgnoreCase)) continue;
                    lnk.TargetPath = ExePath;
                    lnk.WorkingDirectory = Path.GetDirectoryName(ExePath);
                    lnk.IconLocation = ExePath + ",0";
                    lnk.Save();
                    Log.Write("repointed " + path);
                }
                catch (Exception ex) { Log.Write("couldn't repair " + path + ": " + ex.Message); }
            }
            try { if (StartsWithWindows) StartsWithWindows = true; } catch { }
        }

        public static bool AnyConfigured()
        {
            return File.Exists(StartMenuShortcut) || File.Exists(DesktopShortcut) || File.Exists(HotkeyShortcut) || StartsWithWindows;
        }
    }
}
