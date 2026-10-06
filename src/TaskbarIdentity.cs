using System;
using System.Runtime.InteropServices;

namespace GhostMode
{
    /// <summary>
    /// Gives the window its own taskbar identity (name, icon, relaunch command) so it shows as "Ghost Mode"
    /// however it was started (e.g. via the Ctrl+Alt+I "--toggle" shortcut, whose name Windows would otherwise borrow).
    /// </summary>
    static class TaskbarIdentity
    {
        public const string AppId = "GhostMode.App";

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            int GetCount(out uint count);
            int GetAt(uint index, out PropertyKey key);
            int GetValue(ref PropertyKey key, out PropVariant value);
            int SetValue(ref PropertyKey key, ref PropVariant value);
            int Commit();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct PropertyKey { public Guid FormatId; public uint PropertyId; }

        [StructLayout(LayoutKind.Explicit)]
        struct PropVariant { [FieldOffset(0)] public ushort VarType; [FieldOffset(8)] public IntPtr Pointer; [FieldOffset(16)] public IntPtr Pad; }

        [DllImport("shell32.dll")]
        static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

        static readonly Guid AppUserModelFormat = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

        public static void Apply(IntPtr hwnd, string displayName, string exePath)
        {
            var iid = typeof(IPropertyStore).GUID;
            IPropertyStore store;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) != 0 || store == null) return;
            try
            {
                Set(store, 5, AppId);                        // System.AppUserModel.ID
                Set(store, 2, "\"" + exePath + "\"");        // RelaunchCommand
                Set(store, 3, exePath + ",0");               // RelaunchIconResource
                Set(store, 4, displayName);                  // RelaunchDisplayNameResource
                store.Commit();
            }
            finally { Marshal.ReleaseComObject(store); }
        }

        static void Set(IPropertyStore store, uint pid, string value)
        {
            var key = new PropertyKey { FormatId = AppUserModelFormat, PropertyId = pid };
            var v = new PropVariant { VarType = 31 /* VT_LPWSTR */, Pointer = Marshal.StringToCoTaskMemUni(value) };
            try { store.SetValue(ref key, ref v); }
            finally { Marshal.FreeCoTaskMem(v.Pointer); }
        }
    }
}
