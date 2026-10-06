using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GhostMode
{
    public abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Raise(params string[] names)
        {
            var h = PropertyChanged;
            if (h == null) return;
            foreach (var n in names) h(this, new PropertyChangedEventArgs(n));
        }
    }

    public class RelayCommand : ICommand
    {
        readonly Action run; readonly Func<bool> can;
        public RelayCommand(Action run, Func<bool> can) { this.run = run; this.can = can; }
        public event EventHandler CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
        public bool CanExecute(object p) { return can == null || can(); }
        public void Execute(object p) { run(); }
    }

    static class Palette
    {
        public static readonly Brush Online = Make("#23A55A"), Away = Make("#F0B232"), Busy = Make("#F23F43"),
            Hidden = Make("#80848E"), Unknown = Make("#4E5058"), Blurple = Make("#5865F2"), Muted = Make("#949BA4"), Error = Make("#FA777C");
        static Brush Make(string hex) { var b = (Brush)new BrushConverter().ConvertFromString(hex); b.Freeze(); return b; }
    }

    /// <summary>One row in the app list.</summary>
    public class AppItem : Observable
    {
        public IPresenceProvider Provider { get; private set; }
        readonly MainViewModel owner;

        public AppItem(IPresenceProvider provider, MainViewModel owner, bool userEnabled, SavedState saved)
        {
            Provider = provider; this.owner = owner; this.userEnabled = userEnabled;
            if (saved != null)
            {
                Presence p;
                if (Enum.TryParse(saved.State, out p)) { state = p; stateAt = saved.At; }
            }
            SetOnlineCommand = new RelayCommand(() => owner.SetOne(this, false), () => CanSetIndividually);
            SetInvisibleCommand = new RelayCommand(() => owner.SetOne(this, true), () => CanSetIndividually);
        }

        public string Name { get { return Provider.DisplayName; } }
        public string Caveat { get { return Provider.Caveat; } }
        public Visibility CaveatVisibility { get { return string.IsNullOrEmpty(Caveat) ? Visibility.Collapsed : Visibility.Visible; } }
        public ICommand SetOnlineCommand { get; private set; }
        public ICommand SetInvisibleCommand { get; private set; }

        ImageSource icon;
        public ImageSource Icon { get { return icon; } set { icon = value; Raise("Icon", "InitialVisibility"); } }
        public string Initial { get { return Name.Substring(0, 1); } }
        public Visibility InitialVisibility { get { return icon == null ? Visibility.Visible : Visibility.Collapsed; } }

        Availability availability = Availability.NotRunning;
        public Availability Availability
        {
            get { return availability; }
            set { if (availability == value) return; availability = value; RaiseAll(); }
        }
        public bool IsAvailable { get { return availability == Availability.Running; } }

        bool userEnabled;
        /// <summary>The switch: what the user chose, shown as off while the app isn't running.</summary>
        public bool Included
        {
            get { return userEnabled && IsAvailable; }
            set { if (!IsAvailable) return; userEnabled = value; owner.SaveEnabled(this, value); RaiseAll(); }
        }
        public bool UserEnabled { get { return userEnabled; } }

        Presence state = Presence.Unknown;
        DateTime? stateAt;
        bool live;
        public Presence State { get { return state; } }
        public bool IsHidden { get { return state == Presence.Invisible || state == Presence.Offline; } }

        /// <summary>Record a reading. Live readings come straight from the app; others are remembered from earlier.</summary>
        /// <param name="persist">Save even if unchanged (results of a set or a thorough check).</param>
        public void Update(Presence p, bool isLive, bool persist)
        {
            bool changed = p != state;
            state = p; live = isLive; stateAt = DateTime.Now;
            if (changed || persist) owner.SaveState(this, p, stateAt.Value);
            RaiseAll();
        }
        public void Refresh() { RaiseAll(); }
        public void MarkStale() { if (!live) return; live = false; RaiseAll(); }

        bool busy;
        public bool IsBusy { get { return busy; } set { busy = value; RaiseAll(); } }
        string error;
        public string Error { get { return error; } set { error = value; RaiseAll(); } }

        public bool CanSetIndividually { get { return IsAvailable && !busy && !owner.IsBusy; } }
        public double ContentOpacity { get { return IsAvailable ? 1.0 : 0.45; } }
        public Visibility BusyVisibility { get { return busy ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility SwitchVisibility { get { return busy ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility ChevronVisibility { get { return CanSetIndividually ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility BadgeVisibility { get { return IsAvailable && state != Presence.Unknown ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility RingVisibility { get { return IsHidden ? Visibility.Visible : Visibility.Collapsed; } }
        public string SwitchTooltip
        {
            get
            {
                if (availability == Availability.NotInstalled) return Name + " isn't installed";
                if (availability == Availability.NotRunning) return Name + " isn't running";
                return Included ? "Included when toggling" : "Skipped when toggling";
            }
        }

        public Brush StateBrush
        {
            get
            {
                switch (state)
                {
                    case Presence.Online: return Palette.Online;
                    case Presence.Away: return Palette.Away;
                    case Presence.Busy: return Palette.Busy;
                    case Presence.Invisible: case Presence.Offline: return Palette.Hidden;
                    default: return Palette.Unknown;
                }
            }
        }

        public static string Describe(Presence p)
        {
            switch (p)
            {
                case Presence.Online: return "Online";
                case Presence.Away: return "Away";
                case Presence.Busy: return "Do Not Disturb";
                case Presence.Invisible: return "Invisible";
                case Presence.Offline: return "Offline";
                default: return "Unknown";
            }
        }

        public string Subtitle
        {
            get
            {
                if (availability == Availability.NotInstalled) return "Not installed";
                if (availability == Availability.NotRunning) return "Not running";
                if (busy) return "Working\u2026";
                if (!string.IsNullOrEmpty(error)) return error;
                if (state == Presence.Unknown) return "Status unknown \u00B7 Check status";
                var text = Describe(state);
                if (!live && stateAt.HasValue) text += " \u00B7 as of " + When(stateAt.Value);
                return text;
            }
        }
        public Brush SubtitleBrush { get { return IsAvailable && !busy && !string.IsNullOrEmpty(error) ? Palette.Error : Palette.Muted; } }

        static string When(DateTime t)
        {
            var today = DateTime.Today;
            if (t.Date == today) return t.ToString("h:mm tt");
            if (t.Date == today.AddDays(-1)) return "yesterday " + t.ToString("h:mm tt");
            return t.ToString("MMM d");
        }

        void RaiseAll()
        {
            Raise("Availability", "IsAvailable", "Included", "State", "IsHidden", "IsBusy", "Error", "CanSetIndividually", "ContentOpacity",
                  "BusyVisibility", "SwitchVisibility", "ChevronVisibility", "BadgeVisibility", "RingVisibility", "SwitchTooltip",
                  "StateBrush", "Subtitle", "SubtitleBrush");
            owner.OnItemChanged();
        }
    }

    public class MainViewModel : Observable
    {
        public ObservableCollection<AppItem> Apps { get; private set; }
        public ICommand ToggleCommand { get; private set; }
        public ICommand GoInvisibleCommand { get; private set; }
        public ICommand GoOnlineCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }

        readonly Settings settings;
        readonly Dispatcher ui;
        readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        readonly DispatcherTimer pollTimer;
        int pending;

        /// <summary>Raised on the UI thread when a toggle triggered from outside the window (hotkey) finishes.</summary>
        public event Action<string, bool> ExternalToggleFinished;

        public MainViewModel(Dispatcher ui)
        {
            this.ui = ui;
            settings = Settings.Load();
            Apps = new ObservableCollection<AppItem>();
            foreach (var p in ProviderRegistry.All())
            {
                bool enabled;
                if (!settings.Enabled.TryGetValue(p.Id, out enabled)) enabled = true;
                SavedState saved;
                settings.LastKnown.TryGetValue(p.Id, out saved);
                Apps.Add(new AppItem(p, this, enabled, saved));
            }
            ToggleCommand = new RelayCommand(() => Toggle(false), () => CanRun);
            GoInvisibleCommand = new RelayCommand(() => SetAll(true, false), () => CanRun);
            GoOnlineCommand = new RelayCommand(() => SetAll(false, false), () => CanRun);
            RefreshCommand = new RelayCommand(Refresh, () => !IsBusy);

            SystemIntegration.RepairPaths();
            Options = new ObservableCollection<OptionItem>
            {
                new OptionItem("Start Menu shortcut", "Find Ghost Mode in Start",
                    () => System.IO.File.Exists(SystemIntegration.StartMenuShortcut),
                    on => { if (on) SystemIntegration.CreateShortcut(SystemIntegration.StartMenuShortcut, null, null); else SystemIntegration.DeleteShortcut(SystemIntegration.StartMenuShortcut); }),
                new OptionItem("Desktop shortcut", "Ghost Mode icon on your desktop",
                    () => System.IO.File.Exists(SystemIntegration.DesktopShortcut),
                    on => { if (on) SystemIntegration.CreateShortcut(SystemIntegration.DesktopShortcut, null, null); else SystemIntegration.DeleteShortcut(SystemIntegration.DesktopShortcut); }),
                new OptionItem("Ctrl+Alt+I hotkey", "Toggle from anywhere, even when Ghost Mode is closed",
                    () => System.IO.File.Exists(SystemIntegration.HotkeyShortcut),
                    on => { if (on) SystemIntegration.CreateShortcut(SystemIntegration.HotkeyShortcut, "--toggle", SystemIntegration.DefaultHotkey); else SystemIntegration.DeleteShortcut(SystemIntegration.HotkeyShortcut); }),
                new OptionItem("Start with Windows", "Starts quietly in the notification area",
                    () => SystemIntegration.StartsWithWindows,
                    on => SystemIntegration.StartsWithWindows = on),
            };
            foreach (var o in Options) o.Changed += RefreshHotkeyHint;
            showSetup = !settings.SetupDone && !SystemIntegration.AnyConfigured();
            FinishSetupCommand = new RelayCommand(() => { settings.SetupDone = true; settings.Save(); ShowSetup = false; }, null);
            RefreshHotkeyHint();

            var worker = new Thread(WorkerLoop) { IsBackground = true, Name = "automation" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();

            Enqueue(LoadIcons);
            Enqueue(Poll);
            pollTimer = new DispatcherTimer(TimeSpan.FromSeconds(4), DispatcherPriority.Background, (s, e) => { if (pending == 0) Enqueue(Poll); }, ui);
            pollTimer.Start();
            footer = "Ready";
        }

        // ---------------------------------------------------------------- worker

        void WorkerLoop()
        {
            // Per-monitor DPI so UI Automation rectangles, window rects and clicks all use physical pixels
            Native.SetThreadDpiAwarenessContext(Native.DPI_PER_MONITOR_V2);
            foreach (var job in queue.GetConsumingEnumerable())
            {
                try { job(); }
                catch (Exception ex) { Log.Write("worker: " + ex); }
                finally { Interlocked.Decrement(ref pending); }
            }
        }

        void Enqueue(Action job) { Interlocked.Increment(ref pending); queue.Add(job); }
        void OnUi(Action a) { ui.Invoke(a); }

        void LoadIcons()
        {
            foreach (var item in Apps.ToList())
            {
                ImageSource img = null;
                try { img = item.Provider.LoadIcon(); } catch (Exception ex) { Log.Write(item.Name + " icon: " + ex.Message); }
                var it = item;
                if (img != null) OnUi(() => it.Icon = img);
            }
        }

        /// <summary>Cheap pass: what's running, and any status readable without touching the apps.</summary>
        void Poll()
        {
            foreach (var item in Apps.ToList())
            {
                var it = item;
                Availability a;
                try { a = it.Provider.Detect(); } catch { a = Availability.NotRunning; }
                Presence? live = null;
                if (a == Availability.Running)
                {
                    try { live = it.Provider.ReadLive(); } catch (Exception ex) { Log.Write(it.Name + " read: " + ex.Message); }
                }
                OnUi(() =>
                {
                    it.Availability = a;
                    if (live.HasValue) it.Update(live.Value, true, false); else it.MarkStale();
                });
            }
        }

        // ---------------------------------------------------------------- actions

        bool busy;
        public bool IsBusy
        {
            get { return busy; }
            private set { busy = value; Raise("IsBusy", "InvisibleText", "OnlineText"); foreach (var a in Apps) a.Refresh(); CommandManager.InvalidateRequerySuggested(); }
        }

        /// <summary>Toggle all included apps. Direction is decided after a fresh poll: if any are visible, hide everything.</summary>
        public void Toggle(bool external) { Run(null, external); }
        public void SetAll(bool invisible, bool external) { Run(invisible, external); }

        void Run(bool? forceInvisible, bool external)
        {
            if (IsBusy) return;
            IsBusy = true;
            Footer = "Checking\u2026";
            var restoreTo = Native.GetForegroundWindow();
            Enqueue(Poll);
            Enqueue(() =>
            {
                List<AppItem> targets = null; bool invisible = true;
                OnUi(() =>
                {
                    targets = Apps.Where(a => a.Included).ToList();
                    invisible = forceInvisible ?? targets.Any(a => !a.IsHidden);
                    Footer = invisible ? "Going invisible\u2026" : "Going online\u2026";
                });
                var results = new List<string>(); int failures = 0;
                foreach (var item in targets)
                {
                    var r = Apply(item, invisible);
                    if (r != null) { failures++; results.Add(item.Name + ": " + r); }
                }
                Native.Focus(restoreTo);
                OnUi(() =>
                {
                    IsBusy = false;
                    string summary = failures == 0
                        ? (invisible ? "Invisible on " : "Online on ") + Plural(targets.Count, "app") + " \u00B7 " + DateTime.Now.ToString("h:mm tt")
                        : Plural(failures, "app") + " failed: " + string.Join("; ", results);
                    Footer = summary;
                    if (external && ExternalToggleFinished != null) ExternalToggleFinished(summary, invisible);
                });
            });
        }

        public void SetOne(AppItem item, bool invisible)
        {
            if (IsBusy) return;
            IsBusy = true;
            var restoreTo = Native.GetForegroundWindow();
            Footer = (invisible ? "Hiding on " : "Showing on ") + item.Name + "\u2026";
            Enqueue(() =>
            {
                var r = Apply(item, invisible);
                Native.Focus(restoreTo);
                OnUi(() => { IsBusy = false; Footer = r == null ? item.Name + ": " + (invisible ? "invisible" : "online") : item.Name + ": " + r; });
            });
        }

        /// <summary>Runs one provider's Set on the worker. Returns null on success or an error message.</summary>
        string Apply(AppItem item, bool invisible)
        {
            OnUi(() => { item.Error = null; item.IsBusy = true; });
            try
            {
                var res = item.Provider.Set(invisible);
                Log.Write(item.Name + ": " + res.Detail);
                OnUi(() => { item.IsBusy = false; item.Update(res.State, true, true); });
                return null;
            }
            catch (Exception ex)
            {
                Log.Write(item.Name + " failed: " + ex);
                var msg = ex.Message;
                OnUi(() => { item.IsBusy = false; item.Error = "Couldn't change: " + msg; });
                return msg;
            }
        }

        /// <summary>Thorough status check, briefly opening apps whose status can't be read in the background.</summary>
        void Refresh()
        {
            if (IsBusy) return;
            IsBusy = true;
            Footer = "Checking status\u2026";
            var restoreTo = Native.GetForegroundWindow();
            Enqueue(Poll);
            Enqueue(() =>
            {
                foreach (var item in Apps.ToList())
                {
                    var it = item;
                    if (!it.IsAvailable) continue;
                    if (it.Provider.ReadLive().HasValue) continue; // already live, no need to disturb it
                    OnUi(() => { it.Error = null; it.IsBusy = true; });
                    try
                    {
                        var p = it.Provider.Probe();
                        OnUi(() => { it.IsBusy = false; if (p.HasValue) it.Update(p.Value, true, true); });
                    }
                    catch (Exception ex)
                    {
                        Log.Write(it.Name + " probe failed: " + ex);
                        var msg = ex.Message;
                        OnUi(() => { it.IsBusy = false; it.Error = "Couldn't check: " + msg; });
                    }
                }
                Native.Focus(restoreTo);
                OnUi(() => { IsBusy = false; Footer = "Status checked \u00B7 " + DateTime.Now.ToString("h:mm tt"); });
            });
        }

        // ---------------------------------------------------------------- persistence (called by AppItem on the UI thread)

        public void SaveEnabled(AppItem item, bool value) { settings.Enabled[item.Provider.Id] = value; settings.Save(); }

        // ---------------------------------------------------------------- extras (shortcuts, hotkey, startup)

        public ObservableCollection<OptionItem> Options { get; private set; }
        public ICommand FinishSetupCommand { get; private set; }

        bool showSetup;
        /// <summary>First-run card offering the extras; shown until dismissed, unless some are already set up.</summary>
        public bool ShowSetup { get { return showSetup; } set { showSetup = value; Raise("ShowSetup", "SetupVisibility"); } }
        public Visibility SetupVisibility { get { return showSetup ? Visibility.Visible : Visibility.Collapsed; } }

        /// <summary>Re-read every option from the system (shortcuts can be deleted or edited outside the app).</summary>
        public void RefreshOptions()
        {
            foreach (var o in Options) o.Refresh();
            RefreshHotkeyHint();
        }

        string hotkeyHint;
        public string HotkeyHint { get { return hotkeyHint; } }
        public Visibility HotkeyHintVisibility { get { return hotkeyHint == null ? Visibility.Collapsed : Visibility.Visible; } }

        void RefreshHotkeyHint()
        {
            var key = SystemIntegration.CurrentHotkey();
            hotkeyHint = key == null ? null : key + " toggles from anywhere";
            Raise("HotkeyHint", "HotkeyHintVisibility");
        }

        /// <summary>True the first time the window is sent to the tray (ever), so the "still running" hint shows once.</summary>
        public bool ConsumeTrayHint()
        {
            if (settings.TrayHintShown) return false;
            settings.TrayHintShown = true;
            settings.Save();
            return true;
        }
        public void SaveState(AppItem item, Presence p, DateTime at)
        {
            settings.LastKnown[item.Provider.Id] = new SavedState { State = p.ToString(), At = at };
            settings.Save();
        }

        // ---------------------------------------------------------------- summary bindings

        public void OnItemChanged()
        {
            Raise("Headline", "Subline", "ShowGoInvisible", "ShowGoOnline", "GoInvisibleVisibility", "GoOnlineVisibility", "IncludedSummary");
            CommandManager.InvalidateRequerySuggested();
        }

        IEnumerable<AppItem> Included { get { return Apps.Where(a => a.Included); } }

        public string Headline
        {
            get
            {
                var inc = Included.ToList();
                if (inc.Count == 0) return "Nothing to toggle";
                int hidden = inc.Count(a => a.IsHidden);
                if (hidden == inc.Count) return "You're invisible";
                if (hidden == 0) return "You're visible";
                return "Partly visible";
            }
        }

        public string Subline
        {
            get
            {
                var inc = Included.ToList();
                if (inc.Count == 0)
                    return Apps.Any(a => a.IsAvailable) ? "Switch on an app below" : "None of your apps are running";
                int hidden = inc.Count(a => a.IsHidden);
                if (hidden == inc.Count) return "Appearing offline on " + Plural(inc.Count, "app");
                int unknown = inc.Count(a => a.State == Presence.Unknown);
                var names = inc.Where(a => !a.IsHidden && a.State != Presence.Unknown).Select(a => a.Name).ToList();
                var s = names.Count > 0 ? "Showing online on " + string.Join(", ", names) : "";
                if (unknown > 0) s += (s.Length > 0 ? " \u00B7 " : "") + Plural(unknown, "app") + " unknown";
                return s;
            }
        }

        public bool CanRun { get { return !IsBusy && Apps.Any(a => a.Included); } }

        // One action button normally ("Go invisible" while anything shows you online, "Go online" once everything is
        // hidden); both side by side when the included apps are mixed, so either direction is one click.
        public bool ShowGoInvisible { get { var inc = Included.ToList(); return inc.Count == 0 || inc.Any(a => !a.IsHidden); } }
        public bool ShowGoOnline { get { return Included.Any(a => a.IsHidden); } }
        public Visibility GoInvisibleVisibility { get { return ShowGoInvisible ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility GoOnlineVisibility { get { return ShowGoOnline ? Visibility.Visible : Visibility.Collapsed; } }
        public string InvisibleText { get { return IsBusy ? "Working\u2026" : "Go invisible"; } }
        public string OnlineText { get { return IsBusy ? "Working\u2026" : "Go online"; } }

        public string IncludedSummary
        {
            get
            {
                int notRunning = Apps.Count(a => !a.IsAvailable), inc = Apps.Count(a => a.Included);
                return inc + " INCLUDED" + (notRunning > 0 ? ", " + notRunning + " NOT RUNNING" : "");
            }
        }

        string footer;
        public string Footer { get { return footer; } set { footer = value; Raise("Footer"); } }

        static string Plural(int n, string word) { return n + " " + word + (n == 1 ? "" : "s"); }
    }

    /// <summary>One switch in the settings menu / first-run card. State is always read back from the system.</summary>
    public class OptionItem : Observable
    {
        readonly Func<bool> read;
        readonly Action<bool> apply;
        public event Action Changed;

        public OptionItem(string title, string description, Func<bool> read, Action<bool> apply)
        {
            Title = title; Description = description; this.read = read; this.apply = apply;
        }

        public string Title { get; private set; }
        public string Description { get; private set; }

        public bool IsOn
        {
            get { try { return read(); } catch { return false; } }
            set
            {
                try { apply(value); error = null; }
                catch (Exception ex) { Log.Write(Title + ": " + ex); error = "Couldn't change: " + ex.Message; }
                Refresh();
                if (Changed != null) Changed();
            }
        }

        string error;
        public string Detail { get { return error ?? Description; } }
        public Brush DetailBrush { get { return error != null ? Palette.Error : Palette.Muted; } }

        public void Refresh() { Raise("IsOn", "Detail", "DetailBrush"); }
    }}
