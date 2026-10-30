using System.Windows;
using StremioReverso.Core;

namespace StremioReverso.UI;

/// A normal, focusable window for logging in to Reverso; it shares the popup's browser profile.
public partial class SignInWindow : Window
{
    public SignInWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try
            {
                await Web.EnsureCoreWebView2Async(await Browser.Shared);
                Browser.OpenNewWindowsExternally(Web.CoreWebView2);
                Web.CoreWebView2.Navigate(ReversoUrls.SignIn);
            }
            catch (Exception e) { Log.Error("sign-in", e); }
        };
    }
}
