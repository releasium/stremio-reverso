using System.Diagnostics;
using System.Runtime.InteropServices;
using StremioReverso.Core;
using StremioReverso.Win32;

namespace StremioReverso.Stremio;

/// The Stremio 5 desktop window (stremio-shell-ng.exe).
internal sealed class StremioWindow
{
    const string ProcessName = "stremio-shell-ng";
    IntPtr _hwnd;
    DateTime _nextLookup;

    IntPtr Handle
    {
        get
        {
            if (_hwnd != IntPtr.Zero && Native.IsWindow(_hwnd)) return _hwnd;
            if (DateTime.UtcNow < _nextLookup) return IntPtr.Zero;
            _nextLookup = DateTime.UtcNow.AddSeconds(2);
            _hwnd = IntPtr.Zero;
            foreach (var p in Process.GetProcessesByName(ProcessName))
                using (p) if (_hwnd == IntPtr.Zero) _hwnd = p.MainWindowHandle;
            return _hwnd;
        }
    }

    public Box? Bounds =>
        Handle != IntPtr.Zero &&
        Native.DwmGetWindowAttribute(_hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<Native.RECT>()) == 0
            ? Native.ToBox(r)
            : null;

    public bool IsForeground =>
        Handle != IntPtr.Zero && Native.GetAncestor(Native.GetForegroundWindow(), Native.GA_ROOT) == _hwnd;

    public void Activate()
    {
        if (Handle != IntPtr.Zero) Native.SetForegroundWindow(_hwnd);
    }
}
