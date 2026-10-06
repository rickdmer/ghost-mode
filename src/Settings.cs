using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace GhostMode
{
    [DataContract]
    public class SavedState
    {
        [DataMember] public string State;
        [DataMember] public DateTime At;
    }

    /// <summary>Per-app on/off choices and last known states, in %APPDATA%\GhostMode\settings.json.</summary>
    [DataContract]
    public class Settings
    {
        [DataMember] public Dictionary<string, bool> Enabled;
        [DataMember] public Dictionary<string, SavedState> LastKnown;
        [DataMember] public bool TrayHintShown;
        [DataMember] public bool SetupDone;

        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GhostMode");
        static readonly string FilePath = Path.Combine(Folder, "settings.json");
        static readonly object Gate = new object();

        static DataContractJsonSerializer Serializer()
        {
            return new DataContractJsonSerializer(typeof(Settings), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        }

        public static Settings Load()
        {
            Settings s = null;
            try
            {
                if (File.Exists(FilePath))
                    using (var fs = File.OpenRead(FilePath)) s = (Settings)Serializer().ReadObject(fs);
            }
            catch (Exception ex) { Log.Write("settings unreadable, starting fresh: " + ex.Message); }
            s = s ?? new Settings();
            if (s.Enabled == null) s.Enabled = new Dictionary<string, bool>();
            if (s.LastKnown == null) s.LastKnown = new Dictionary<string, SavedState>();
            return s;
        }

        public void Save()
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    var tmp = FilePath + ".tmp";
                    using (var fs = File.Create(tmp)) Serializer().WriteObject(fs, this);
                    if (File.Exists(FilePath)) File.Delete(FilePath);
                    File.Move(tmp, FilePath);
                }
                catch (Exception ex) { Log.Write("couldn't save settings: " + ex.Message); }
            }
        }
    }

    public static class Log
    {
        static readonly string FilePath = Path.Combine(Settings.Folder, "log.txt");
        static readonly object Gate = new object();

        public static void Write(string message)
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Settings.Folder);
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    // Don't lose the message (or the reason the log can't be written) silently
                    try
                    {
                        File.AppendAllText(Path.Combine(Path.GetTempPath(), "GhostMode-log.txt"),
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [log unwritable: " + ex.Message + "]  " + message + Environment.NewLine, Encoding.UTF8);
                    }
                    catch { }
                }
            }
        }
    }
}
