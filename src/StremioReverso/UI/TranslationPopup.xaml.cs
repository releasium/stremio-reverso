using System.Windows;
using StremioReverso.Core;

namespace StremioReverso.UI;

/// Shows the Reverso page for the hovered word or line in an embedded browser.
public partial class TranslationPopup : Window
{
    public event Action<bool>? PinnedChanged;

    readonly Task _ready;
    string _url = "";

    public TranslationPopup()
    {
        InitializeComponent();
        WindowPlacement.MakeOverlay(this);
        _ready = InitBrowserAsync();
    }

    async Task InitBrowserAsync()
    {
        await Web.EnsureCoreWebView2Async(await Browser.Shared);
        Browser.OpenNewWindowsExternally(Web.CoreWebView2);
    }

    public async void ShowPage(string heading, string url, Box anchor)
    {
        Heading.Text = heading;
        Present(anchor);
        try
        {
            await _ready;
            if (url == _url) return;
            _url = url;
            Web.CoreWebView2.Navigate(url);
        }
        catch (Exception e) { Log.Error("browser", e); }
    }

    public void HidePopup()
    {
        PinButton.IsChecked = false;
        Hide();
    }

    void Present(Box anchor)
    {
        if (!IsVisible) Show();
        double s = WindowPlacement.Scale(this);
        double w = Width * s, h = Height * s;
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)anchor.X, (int)anchor.Y)).WorkingArea;
        double x = Math.Clamp(anchor.X + anchor.W / 2 - w / 2, screen.Left, Math.Max(screen.Left, screen.Right - w));
        double y = anchor.Y - h - 10;
        if (y < screen.Top) y = anchor.Bottom + 10;
        WindowPlacement.Move(this, x, y);
    }

    void OnPin(object sender, RoutedEventArgs e) => PinnedChanged?.Invoke(PinButton.IsChecked == true);

    void OnClose(object sender, RoutedEventArgs e)
    {
        HidePopup();
        PinnedChanged?.Invoke(false);
    }

    void OnOpenExternal(object sender, RoutedEventArgs e)
    {
        if (_url.Length > 0) Browser.OpenExternal(_url);
    }
}
