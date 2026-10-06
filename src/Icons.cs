using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GhostMode
{
    /// <summary>Loads app icons (from an exe, .ico or .png) as frozen WPF images.</summary>
    public static class Icons
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, uint count, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public static ImageSource FromExe(string path, int size = 64)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var handles = new IntPtr[1]; var ids = new int[1];
            if (PrivateExtractIcons(path, 0, size, size, handles, ids, 1, 0) == 0 || handles[0] == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(handles[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { DestroyIcon(handles[0]); }
        }

        public static ImageSource FromFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
                    frame.Freeze();
                    return frame;
                }
            }
            catch { return null; }
        }

        public static ImageSource FromResource(string name)
        {
            using (var s = typeof(Icons).Assembly.GetManifestResourceStream(name))
            {
                if (s == null) return null;
                var decoder = BitmapDecoder.Create(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
                frame.Freeze();
                return frame;
            }
        }
    }
}
