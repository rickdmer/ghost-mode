using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;

namespace GhostMode
{
    public static class App
    {
        const string MutexName = "GhostMode.SingleInstance";
        const string ToggleEventName = "GhostMode.Toggle";
        const string ShowEventName = "GhostMode.Show";
        static bool welcome;

        /// <summary>
        /// GhostMode.exe            open the window (or bring the running one forward)
        /// GhostMode.exe --toggle   toggle everything; used by the Ctrl+Alt+I shortcut. Starts in the tray if not running.
        /// GhostMode.exe --tray     start in the tray (used by "Start with Windows")
        /// GhostMode.exe --welcome  show the first-run welcome card again
        /// </summary>
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string id);

        [STAThread]
        public static int Main(string[] args)
        {
            // Own taskbar identity, so it's labelled "Ghost Mode" whichever shortcut launched it
            SetCurrentProcessExplicitAppUserModelID(TaskbarIdentity.AppId);

            bool toggle = args.Any(a => a.Equals("--toggle", StringComparison.OrdinalIgnoreCase));
            bool tray = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
            welcome = args.Any(a => a.Equals("--welcome", StringComparison.OrdinalIgnoreCase));

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    // Already running: hand the request to that instance
                    // (--tray is the start-with-Windows launch; if we're somehow already running, there's nothing to do)
                    if (tray && !toggle) return 0;
                    try { using (var ev = EventWaitHandle.OpenExisting(toggle ? ToggleEventName : ShowEventName)) ev.Set(); }
                    catch (Exception ex) { Log.Write("couldn't signal running instance: " + ex.Message); }
                    return 0;
                }
                try { return Run(toggle, tray); }
                catch (Exception ex) { Log.Write("fatal: " + ex); MessageBox.Show(ex.ToString(), "Ghost Mode crashed"); return 1; }
            }
        }

        static int Run(bool toggleOnStart, bool startInTray)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += (s, e) => { Log.Write("ui: " + e.Exception); e.Handled = true; };

            Window window;
            using (var s = typeof(App).Assembly.GetManifestResourceStream("GhostMode.MainWindow.xaml"))
                window = (Window)XamlReader.Load(s);
            app.MainWindow = window;

            var appIcon = Icons.FromResource("GhostMode.app.ico");
            window.Icon = appIcon;
            ((System.Windows.Controls.Image)window.FindName("TitleIcon")).Source = appIcon;
            ((System.Windows.Controls.Image)window.FindName("HeroIcon")).Source = appIcon;
            ((System.Windows.Controls.Image)window.FindName("SetupIcon")).Source = appIcon;
            var version = typeof(App).Assembly.GetName().Version;
            ((TextBlock)window.FindName("VersionText")).Text = "Ghost Mode " + version.Major + "." + version.Minor;

            var vm = new MainViewModel(window.Dispatcher);
            if (welcome) vm.ShowSetup = true;
            window.DataContext = vm;

            // Left-clicking an app's status line opens its Online / Invisible menu
            ((ItemsControl)window.FindName("AppList")).AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                var b = e.OriginalSource as Button;
                if (b == null || !"status".Equals(b.Tag) || b.ContextMenu == null) return;
                b.ContextMenu.PlacementTarget = b;
                b.ContextMenu.Placement = PlacementMode.Bottom;
                b.ContextMenu.DataContext = b.DataContext;
                b.ContextMenu.IsOpen = true;
            }));

            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                TaskbarIdentity.Apply(hwnd, "Ghost Mode", typeof(App).Assembly.Location);
                int round = 2; Native.DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, 4);
                int border = 0x00221F1E; Native.DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR */, ref border, 4);
            };

            // Minimise and close both send the window to the tray; only the tray menu's Exit quits
            bool exiting = false;
            TrayIcon tray = null;
            Action showWindow = () =>
            {
                window.Show();
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate();
            };
            Action hideToTray = () =>
            {
                window.Hide();
                if (vm.ConsumeTrayHint())
                    tray.Balloon("Ghost Mode is still running", "It's in the notification area. Right-click the icon to exit.");
            };
            Action exit = () => { exiting = true; window.Close(); };

            tray = new TrayIcon(showWindow, () => vm.SetAll(true, true), () => vm.SetAll(false, true),
                                () => vm.ShowGoInvisible, () => vm.ShowGoOnline, () => vm.CanRun, exit);
            tray.SetTooltip("Ghost Mode \u2013 " + vm.Headline);
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "Headline") tray.SetTooltip("Ghost Mode \u2013 " + vm.Headline); };

            ((Button)window.FindName("MinimizeButton")).Click += (s, e) => hideToTray();

            // Gear: settings flyout. It closes itself on any outside click, including on the gear, so ignore a click
            // that lands right after it closed rather than immediately reopening it.
            var popup = (Popup)window.FindName("SettingsPopup");
            var closedAt = DateTime.MinValue;
            popup.Closed += (s, e) => closedAt = DateTime.Now;
            ((Button)window.FindName("SettingsButton")).Click += (s, e) =>
            {
                if ((DateTime.Now - closedAt).TotalMilliseconds < 300) return;
                vm.RefreshOptions();
                popup.IsOpen = true;
            };
            window.Activated += (s, e) => vm.RefreshOptions();
            ((Button)window.FindName("CloseButton")).Click += (s, e) => hideToTray();
            window.StateChanged += (s, e) => { if (window.WindowState == WindowState.Minimized) hideToTray(); };  // Win+Down etc.
            window.Closing += (s, e) => { if (!exiting) { e.Cancel = true; hideToTray(); } };                   // Alt+F4
            window.Closed += (s, e) => tray.Dispose();

            vm.ExternalToggleFinished += (summary, invisible) =>
            {
                if (!window.IsVisible || !window.IsActive) tray.Balloon(invisible ? "You're invisible" : "You're online", summary);
            };

            ListenForOtherInstances(window, vm, showWindow);

            // Started by the hotkey: stay in the tray and just toggle. Started with Windows: just sit in the tray.
            if (toggleOnStart) vm.Toggle(true);
            else if (!startInTray) window.Show();
            return app.Run();
        }

        static void ListenForOtherInstances(Window window, MainViewModel vm, Action showWindow)
        {
            var toggleEv = new EventWaitHandle(false, EventResetMode.AutoReset, ToggleEventName);
            var showEv = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var t = new Thread(() =>
            {
                var handles = new WaitHandle[] { toggleEv, showEv };
                while (true)
                {
                    int i = WaitHandle.WaitAny(handles);
                    if (i == 0) window.Dispatcher.BeginInvoke(new Action(() => vm.Toggle(true)));
                    else window.Dispatcher.BeginInvoke(showWindow);
                }
            }) { IsBackground = true, Name = "instance-listener" };
            t.Start();
        }
    }
}
