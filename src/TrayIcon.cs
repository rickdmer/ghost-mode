using System;
using System.Drawing;
using System.Windows.Forms;

namespace GhostMode
{
    /// <summary>
    /// Ghost Mode's notification-area icon. Left-click opens the window; right-click offers Open / Go invisible / Go online / Exit.
    /// Also used for the balloons shown after a hotkey toggle or the first time the window goes to the tray.
    /// </summary>
    sealed class TrayIcon : IDisposable
    {
        readonly NotifyIcon icon;

        /// <summary>showInvisible / showOnline say which of the two actions to offer right now (both when apps are mixed).</summary>
        public TrayIcon(Action open, Action goInvisible, Action goOnline, Func<bool> showInvisible, Func<bool> showOnline,
                        Func<bool> canRun, Action exit)
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new ToolStripProfessionalRenderer(new DarkColors()) { RoundedEdges = false },
                ShowImageMargin = false,
                Font = new Font("Segoe UI", 9.5f),
                Padding = new Padding(2, 4, 2, 4),
            };
            var openItem = Item("Open Ghost Mode", open);
            openItem.Font = new Font(menu.Font, FontStyle.Bold);
            var invisibleItem = Item("Go invisible", goInvisible);
            var onlineItem = Item("Go online", goOnline);
            menu.Items.Add(openItem);
            menu.Items.Add(invisibleItem);
            menu.Items.Add(onlineItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Exit", exit));
            menu.Opening += (s, e) =>
            {
                invisibleItem.Visible = showInvisible();
                onlineItem.Visible = showOnline();
                invisibleItem.Enabled = onlineItem.Enabled = canRun();
            };

            icon = new NotifyIcon { ContextMenuStrip = menu, Text = "Ghost Mode" };
            using (var s = typeof(TrayIcon).Assembly.GetManifestResourceStream("GhostMode.app.ico"))
                icon.Icon = new Icon(s, SystemInformation.SmallIconSize);
            icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) open(); };
            icon.BalloonTipClicked += (s, e) => open();
            icon.Visible = true;
        }

        static ToolStripMenuItem Item(string text, Action onClick)
        {
            var item = new ToolStripMenuItem(text) { ForeColor = Color.FromArgb(0xDB, 0xDE, 0xE1), Padding = new Padding(4, 3, 16, 3) };
            item.Click += (s, e) => onClick();
            return item;
        }

        /// <summary>Hover text; Windows caps it at 63 characters.</summary>
        public void SetTooltip(string text)
        {
            icon.Text = text.Length > 63 ? text.Substring(0, 62) + "…" : text;
        }

        public void Balloon(string title, string text)
        {
            icon.ShowBalloonTip(4000, title, text, ToolTipIcon.None);
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
        }

        /// <summary>Discord-style dark menu colours.</summary>
        class DarkColors : ProfessionalColorTable
        {
            static readonly Color Bg = Color.FromArgb(0x11, 0x12, 0x14), Hover = Color.FromArgb(0x58, 0x65, 0xF2), Line = Color.FromArgb(0x2B, 0x2D, 0x31);
            public override Color ToolStripDropDownBackground { get { return Bg; } }
            public override Color ImageMarginGradientBegin { get { return Bg; } }
            public override Color ImageMarginGradientMiddle { get { return Bg; } }
            public override Color ImageMarginGradientEnd { get { return Bg; } }
            public override Color MenuBorder { get { return Line; } }
            public override Color MenuItemBorder { get { return Hover; } }
            public override Color MenuItemSelected { get { return Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Hover; } }
            public override Color SeparatorDark { get { return Line; } }
            public override Color SeparatorLight { get { return Line; } }
        }
    }
}
