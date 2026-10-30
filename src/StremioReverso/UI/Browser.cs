using System.Diagnostics;
using System.IO;
using Microsoft.Web.WebView2.Core;

namespace StremioReverso.UI;

/// One WebView2 profile for every browser view, so the Reverso sign-in is shared and remembered.
internal static class Browser
{
    static Task<CoreWebView2Environment>? _shared;

    public static Task<CoreWebView2Environment> Shared => _shared ??= CoreWebView2Environment.CreateAsync(null,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StremioReverso", "WebView2"));

    /// Links that want a new window go to the default browser instead.
    public static void OpenNewWindowsExternally(CoreWebView2 web) => web.NewWindowRequested += (_, e) =>
    {
        e.Handled = true;
        OpenExternal(e.Uri);
    };

    public static void OpenExternal(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
