using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using StremioReverso.Core;

namespace StremioReverso.UI;

public partial class TranslationPopup : Window
{
    const string Failed = "Couldn't load translation";
    static readonly Brush SourceBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
    static readonly Brush TargetBrush = new SolidColorBrush(Color.FromRgb(0xA5, 0xD6, 0xA7));

    public event Action<bool>? PinnedChanged;
    string _query = "";

    public TranslationPopup()
    {
        InitializeComponent();
        WindowPlacement.MakeOverlay(this);
    }

    public void ShowLoading(string heading, Box anchor) => Fill(heading, "…", anchor);

    public void ShowWord(string word, WordResult? result, Box anchor)
    {
        Fill(word, result is null ? Failed : string.Join(", ", result.Translations), anchor, () =>
        {
            foreach (var ex in result?.Examples ?? Array.Empty<Example>())
            {
                Examples.Children.Add(Rich(ex.Source, SourceBrush, 8));
                Examples.Children.Add(Rich(ex.Target, TargetBrush, 0));
            }
        });
    }

    public void ShowLine(string line, string? translation, Box anchor) => Fill(line, translation ?? Failed, anchor);

    public void HidePopup()
    {
        PinMark.Visibility = Visibility.Collapsed;
        Hide();
    }

    void Fill(string heading, string translations, Box anchor, Action? addExamples = null)
    {
        _query = heading;
        Heading.Text = heading;
        Translations.Text = translations;
        Examples.Children.Clear();
        addExamples?.Invoke();
        Present(anchor);
    }

    void Present(Box anchor)
    {
        if (!IsVisible) Show();
        UpdateLayout();
        double s = WindowPlacement.Scale(this);
        double w = ActualWidth * s, h = ActualHeight * s;
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)anchor.X, (int)anchor.Y)).WorkingArea;
        double x = Math.Clamp(anchor.X + anchor.W / 2 - w / 2, screen.Left, Math.Max(screen.Left, screen.Right - w));
        double y = anchor.Y - h - 10;
        if (y < screen.Top) y = anchor.Bottom + 10;
        WindowPlacement.Move(this, x, y);
    }

    static TextBlock Rich(string html, Brush brush, double top)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = brush, FontSize = 13, Margin = new Thickness(0, top, 0, 0) };
        foreach (var (text, em) in EmText.Split(html))
            tb.Inlines.Add(em ? new Bold(new Run(text)) : new Run(text));
        return tb;
    }

    void OnBodyClick(object sender, MouseButtonEventArgs e)
    {
        PinMark.Visibility = Visibility.Visible;
        PinnedChanged?.Invoke(true);
    }

    void OnClose(object sender, RoutedEventArgs e)
    {
        HidePopup();
        PinnedChanged?.Invoke(false);
    }

    void OnOpenReverso(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(ReversoClient.ContextPageUrl(_query)) { UseShellExecute = true });
}
