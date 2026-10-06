using System;
using System.Drawing;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Media;
using Color = System.Drawing.Color;
using Point = System.Drawing.Point;

namespace GhostMode
{
    public enum Presence { Unknown, Online, Away, Busy, Invisible, Offline }

    public enum Availability { NotInstalled, NotRunning, Running }

    public class SetResult
    {
        public Presence State;
        public string Detail;
        public SetResult(Presence state, string detail) { State = state; Detail = detail; }
    }

    /// <summary>
    /// One supported app. To add a new app: implement this (usually by deriving from <see cref="ProviderBase"/>)
    /// and add an instance to <see cref="ProviderRegistry.All"/>. The UI is generated from that list.
    /// </summary>
    public interface IPresenceProvider
    {
        /// <summary>Stable key used for saved settings.</summary>
        string Id { get; }
        string DisplayName { get; }
        /// <summary>Short note shown under the app when it's being driven by fragile means (screen positions etc.).</summary>
        string Caveat { get; }
        ImageSource LoadIcon();
        Availability Detect();
        /// <summary>Cheap read with no visible side effects. Null when the state can't be seen right now.</summary>
        Presence? ReadLive();
        /// <summary>Thorough read; may briefly bring the app's window up. Null if it can't tell.</summary>
        Presence? Probe();
        /// <summary>Set appear-offline (true) or online (false). Throws with a readable message on failure.</summary>
        SetResult Set(bool invisible);
    }

    public abstract class ProviderBase : IPresenceProvider
    {
        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public virtual string Caveat { get { return null; } }
        public abstract ImageSource LoadIcon();
        public abstract Availability Detect();
        public virtual Presence? ReadLive() { return null; }
        public virtual Presence? Probe() { return ReadLive(); }
        public abstract SetResult Set(bool invisible);

        // ---------------------------------------------------------------- window handling

        /// <summary>How to put a window back where it was when it had been tucked away in the tray.</summary>
        protected enum TrayReturn { Hide, Close, Minimize }

        /// <summary>
        /// Brings an app window up for automation and puts it back afterwards (tray / minimised / as it was).
        /// <paramref name="find"/> must return the current main window (or null); <paramref name="open"/> asks the app to show it.
        /// </summary>
        protected sealed class WindowSession : IDisposable
        {
            readonly Func<Native.WindowInfo> find;
            readonly TrayReturn trayReturn;
            readonly bool hadWindow, wasVisible, wasMinimized;
            public IntPtr Handle { get; private set; }

            public WindowSession(Func<Native.WindowInfo> find, Action open, TrayReturn trayReturn, int timeoutMs)
            {
                this.find = find;
                this.trayReturn = trayReturn;
                var before = find();
                hadWindow = before != null;
                wasVisible = hadWindow && before.Visible;
                wasMinimized = hadWindow && before.Minimized;
                if (!wasVisible || wasMinimized) open();
                var w = Native.WaitFor(() => { var x = find(); return x != null && x.Visible && !x.Minimized ? x : null; }, timeoutMs);
                if (w == null) throw new InvalidOperationException("window didn't open");
                Handle = w.Handle;
                Native.Focus(Handle);
                Thread.Sleep(400);
            }

            public void Dispose()
            {
                var current = find();
                IntPtr h = current != null ? current.Handle : Handle;
                if (!Native.IsWindow(h)) return;
                if (!wasVisible)
                {
                    switch (trayReturn)
                    {
                        case TrayReturn.Hide: Native.ShowWindow(h, Native.SW_HIDE); break;
                        case TrayReturn.Close: Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); break;
                        default: Native.ShowWindow(h, Native.SW_MINIMIZE); break;
                    }
                }
                else if (wasMinimized) Native.ShowWindow(h, Native.SW_MINIMIZE);
            }
        }

        // ---------------------------------------------------------------- UI Automation

        protected static AutomationElement FindByName(AutomationElement root, string name)
        {
            return root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name));
        }

        protected static AutomationElement FindById(AutomationElement root, string automationId)
        {
            return root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
        }

        protected static AutomationElement FindByNameRegex(AutomationElement root, string pattern)
        {
            foreach (AutomationElement e in root.FindAll(TreeScope.Descendants, Condition.TrueCondition))
                if (System.Text.RegularExpressions.Regex.IsMatch(e.Current.Name ?? "", pattern)) return e;
            return null;
        }

        protected static void ClickElement(AutomationElement el)
        {
            var r = el.Current.BoundingRectangle;
            if (r.IsEmpty) throw new InvalidOperationException("'" + el.Current.Name + "' isn't on screen");
            int x = (int)(r.X + r.Width / 2), y = (int)(r.Y + r.Height / 2);
            // UWP apps (e.g. Xbox) sit under an invisible caption window covering the top strip of the frame, which
            // swallows clicks. If the centre is under it, click further down the element instead.
            for (int tryY = y; tryY < r.Bottom - 2 && Native.IsCaptionOverlay(x, tryY); tryY += 3) y = tryY + 3;
            Native.Click(x, Math.Min(y, (int)r.Bottom - 2));
            Thread.Sleep(300);
        }

        protected static void Activate(AutomationElement el)
        {
            object p;
            if (el.TryGetCurrentPattern(InvokePattern.Pattern, out p)) ((InvokePattern)p).Invoke();
            else if (el.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out p)) ((ExpandCollapsePattern)p).Expand();
            else ClickElement(el);
        }

        // ---------------------------------------------------------------- pixel helpers for apps with no accessibility tree

        /// <summary>A point in the window's client area given as an offset (96-DPI units) from the left or right edge.</summary>
        protected static Point ClientPoint(IntPtr hwnd, int dx, int dy, bool fromRight)
        {
            var c = Native.ClientRect(hwnd);
            double s = Native.Scale(hwnd);
            int x = fromRight ? c.Right - (int)(dx * s) : c.Left + (int)(dx * s);
            return new Point(x, c.Top + (int)(dy * s));
        }

        /// <summary>Reads a pixel from a client-area offset using a PrintWindow snapshot (works while covered, not while hidden).</summary>
        protected static Color? SnapshotPixel(IntPtr hwnd, int dx, int dy, bool fromRight)
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return null;
            using (var bmp = Native.SnapshotClient(hwnd))
            {
                if (bmp == null) return null;
                double s = Native.Scale(hwnd);
                int x = fromRight ? bmp.Width - (int)(dx * s) : (int)(dx * s);
                int y = (int)(dy * s);
                if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) return null;
                return bmp.GetPixel(x, y);
            }
        }

        /// <summary>
        /// Classifies a presence dot colour: green online, amber away, red busy, mid grey invisible.
        /// Dark greys are the page background (window still loading), so they're Unknown rather than Invisible.
        /// </summary>
        protected static Presence ClassifyDot(Color c)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
            int avg = (c.R + c.G + c.B) / 3;
            if (max - min < 28) return avg >= 70 && avg <= 200 ? Presence.Invisible : Presence.Unknown;
            if (c.G > 110 && c.G > c.R + 40) return Presence.Online;
            if (c.R > 150 && c.G > 90 && c.B < 90) return Presence.Away;
            if (c.R > 150 && c.G < 90) return Presence.Busy;
            return Presence.Unknown;
        }

        /// <summary>"R,G,B" of a client-area pixel, for error messages when a status dot can't be recognised.</summary>
        protected static string DescribePixel(IntPtr hwnd, int dx, int dy, bool fromRight)
        {
            try
            {
                var c = SnapshotPixel(hwnd, dx, dy, fromRight);
                return c.HasValue ? "saw colour " + c.Value.R + "," + c.Value.G + "," + c.Value.B : "window couldn't be captured";
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>Polls a dot reader until the page has loaded enough to show a recognisable status.</summary>
        protected static Presence? WaitForDot(Func<Presence?> read, int timeoutMs)
        {
            Presence? result = null;
            Native.WaitUntil(() => { result = read(); return result.HasValue; }, timeoutMs, 150);
            return result;
        }
    }
}
