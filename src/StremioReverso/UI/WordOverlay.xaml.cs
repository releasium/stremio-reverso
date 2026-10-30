using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StremioReverso.Core;

namespace StremioReverso.UI;

/// Sits over the frozen subtitle while paused: one hover target per word, and a ⇄ handle per line.
public partial class WordOverlay : Window
{
    public event Action<OcrLine, OcrWord>? WordHovered;
    public event Action<OcrLine>? LineHovered;

    readonly DispatcherTimer _hoverDelay = new() { Interval = TimeSpan.FromMilliseconds(150) };
    Action? _pending;

    public WordOverlay()
    {
        InitializeComponent();
        WindowPlacement.MakeOverlay(this);
        _hoverDelay.Tick += (_, _) => { _hoverDelay.Stop(); _pending?.Invoke(); };
    }

    public void ShowLines(IReadOnlyList<OcrLine> lines)
    {
        var area = Box.Union(lines.Select(l => new Box(l.Box.X - 6, l.Box.Y - 6, l.Box.W + 48, l.Box.H + 12)));
        WindowPlacement.Place(this, area);
        double s = WindowPlacement.Scale(this);
        Root.Children.Clear();
        foreach (var line in lines)
        {
            foreach (var word in line.Words)
            {
                if (WordNormalizer.Normalize(word.Text) is null) continue;
                AddTarget(word.Box, area, s, null, () => WordHovered?.Invoke(line, word));
            }
            var handle = new Box(line.Box.Right + 8, line.Box.Y + line.Box.H / 2 - 12, 28, 24);
            AddTarget(handle, area, s, "⇄", () => LineHovered?.Invoke(line));
        }
    }

    public new void Hide()
    {
        _hoverDelay.Stop();
        _pending = null;
        base.Hide();
    }

    void AddTarget(Box box, Box area, double s, string? label, Action onHover)
    {
        var border = new Border
        {
            Width = box.W / s + 4,
            Height = box.H / s + 4,
            // Alpha 1 keeps the box hit-testable while staying invisible.
            Background = new SolidColorBrush(Color.FromArgb(label is null ? (byte)1 : (byte)180, 20, 20, 20)),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(3),
            Cursor = Cursors.Hand,
        };
        if (label != null)
            border.Child = new TextBlock
            {
                Text = label, Foreground = Brushes.White, FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        border.MouseEnter += (_, _) =>
        {
            border.BorderBrush = Brushes.Gold;
            _pending = onHover;
            _hoverDelay.Stop();
            _hoverDelay.Start();
        };
        border.MouseLeave += (_, _) =>
        {
            border.BorderBrush = Brushes.Transparent;
            if (_pending == onHover) _hoverDelay.Stop();
        };
        Canvas.SetLeft(border, (box.X - area.X) / s - 2);
        Canvas.SetTop(border, (box.Y - area.Y) / s - 2);
        Root.Children.Add(border);
    }
}
