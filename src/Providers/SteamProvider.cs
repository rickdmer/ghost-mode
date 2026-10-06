using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Microsoft.Win32;

namespace GhostMode
{
    /// <summary>
    /// Steam has a supported URL for this (steam://friends/status/...), and records the current persona state in the
    /// signed-in user's localconfig.vdf, so no UI automation is needed.
    /// </summary>
    public class SteamProvider : ProviderBase
    {
        public override string Id { get { return "steam"; } }
        public override string DisplayName { get { return "Steam"; } }

        static string SteamExe
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var v = k == null ? null : k.GetValue("SteamExe") as string;
                    return string.IsNullOrEmpty(v) ? null : Path.GetFullPath(v);
                }
            }
        }

        public override ImageSource LoadIcon() { return Icons.FromExe(SteamExe); }

        public override Availability Detect()
        {
            if (Native.IsRunning("steam")) return Availability.Running;
            var exe = SteamExe;
            return exe != null && File.Exists(exe) ? Availability.NotRunning : Availability.NotInstalled;
        }

        string cachedPath; DateTime cachedStamp; Presence? cachedState;

        public override Presence? ReadLive()
        {
            var exe = SteamExe;
            if (exe == null) return null;
            int user;
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
            {
                object v = k == null ? null : k.GetValue("ActiveUser");
                user = v is int ? (int)v : 0;
            }
            if (user == 0) return null;
            var path = Path.Combine(Path.GetDirectoryName(exe), "userdata", user.ToString(), "config", "localconfig.vdf");
            if (!File.Exists(path)) return null;
            var stamp = File.GetLastWriteTimeUtc(path);
            if (path == cachedPath && stamp == cachedStamp) return cachedState;

            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs)) text = sr.ReadToEnd();
            var m = Regex.Match(text, @"ePersonaState\\?""\s*:\s*(\d+)");
            Presence? state = null;
            if (m.Success)
            {
                switch (int.Parse(m.Groups[1].Value))
                {
                    case 0: state = Presence.Offline; break;
                    case 1: case 5: case 6: state = Presence.Online; break;
                    case 2: state = Presence.Busy; break;
                    case 3: case 4: state = Presence.Away; break;
                    case 7: state = Presence.Invisible; break;
                }
            }
            cachedPath = path; cachedStamp = stamp; cachedState = state;
            return state;
        }

        public override SetResult Set(bool invisible)
        {
            Process.Start("steam://friends/status/" + (invisible ? "invisible" : "online"));
            var target = invisible ? Presence.Invisible : Presence.Online;
            Presence? after = null;
            Native.WaitUntil(() => { after = ReadLive(); return after == target; }, 5000, 300);
            return new SetResult(target, after == target ? "set " + target.ToString().ToLower() : "sent (Steam hasn't confirmed yet)");
        }
    }
}
