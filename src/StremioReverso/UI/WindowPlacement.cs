using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using StremioReverso.Core;
using StremioReverso.Win32;

namespace StremioReverso.UI;

/// Positions WPF windows in physical pixels and keeps them from ever taking focus.
internal static class WindowPlacement
{
    public static void MakeOverlay(Window w) => w.SourceInitialized += (_, _) =>
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE,
            Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
    };

    public static void Place(Window w, Box px)
    {
        if (!w.IsVisible) w.Show();
        Native.SetWindowPos(Hwnd(w), Native.HWND_TOPMOST, (int)px.X, (int)px.Y, (int)px.W, (int)px.H, Native.SWP_NOACTIVATE);
    }

    public static void Move(Window w, double x, double y) =>
        Native.SetWindowPos(Hwnd(w), Native.HWND_TOPMOST, (int)x, (int)y, 0, 0, Native.SWP_NOACTIVATE | Native.SWP_NOSIZE);

    public static double Scale(Window w) => VisualTreeHelper.GetDpi(w).DpiScaleX;

    public static bool ContainsCursor(Window w, double x, double y) =>
        w.IsVisible && Native.GetWindowRect(Hwnd(w), out var r) && Native.ToBox(r).Contains(x, y);

    public static bool IsForeground(Window w) =>
        w.IsVisible && Native.GetAncestor(Native.GetForegroundWindow(), Native.GA_ROOT) == Hwnd(w);

    static IntPtr Hwnd(Window w) => new WindowInteropHelper(w).EnsureHandle();
}
