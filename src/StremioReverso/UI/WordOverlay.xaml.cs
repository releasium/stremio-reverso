using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StremioReverso.Core;

namespace StremioReverso.UI;

/// Sits over the frozen subtitle while paused: one hover target per word, a ⇄ handle per line,
/// and click-or-drag selection of a run of words.
public partial class WordOverlay : Window
{
    public event Action<OcrLine, OcrWord>? WordHovered;
    /// A line's ⇄ handle was hovered; the box covers the line and its handle.
    public event Action<OcrLine, Box>? LineHovered;
    /// Words picked by click or drag (reading order, joined by spaces) and their screen box.
    public event Action<string, Box>? Selected;

    // Alpha 1 keeps a box hit-testable while staying invisible.
    static readonly Brush Unpicked = new SolidColorBrush(Color.FromArgb(1, 20, 20, 20));
    static readonly Brush Picked = new SolidColorBrush(Color.FromArgb(90, 66, 133, 244));

    readonly DispatcherTimer _hoverDelay = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly List<(OcrWord Word, Border Border)> _words = new();
    Action? _pending;
    int _from = -1, _to = -1; // selected word range (either order); -1 when nothing is selected
    bool _dragging;

    public WordOverlay()
    {
        InitializeComponent();
        WindowPlacement.MakeOverlay(this);
        _hoverDelay.Tick += (_, _) => { _hoverDelay.Stop(); _pending?.Invoke(); };
        Root.MouseMove += OnDragMove;
        Root.MouseLeftButtonUp += OnDragEnd;
    }

    public void ShowLines(IReadOnlyList<OcrLine> lines)
    {
        var area = Box.Union(lines.Select(l => new Box(l.Box.X - 6, l.Box.Y - 6, l.Box.W + 48, l.Box.H + 12)));
        WindowPlacement.Place(this, area);
        double s = WindowPlacement.Scale(this);
        Root.Children.Clear();
        _words.Clear();
        _from = _to = -1;
        foreach (var line in lines)
        {
            foreach (var word in line.Words)
            {
                if (WordNormalizer.Normalize(word.Text) is null) continue;
                int index = _words.Count;
                var border = AddTarget(word.Box, area, s, null, () =>
                {
                    if (IsSelected(index)) return; // keep the selection's popup
                    ClearSelection();
                    WordHovered?.Invoke(line, word);
                });
                border.MouseLeftButtonDown += (_, e) => { StartDrag(index); e.Handled = true; };
                _words.Add((word, border));
            }
            var handle = new Box(line.Box.Right + 8, line.Box.Y + line.Box.H / 2 - 12, 28, 24);
            var lineAndHandle = Box.Union(new[] { line.Box, handle });
            AddTarget(handle, area, s, "⇄", () => LineHovered?.Invoke(line, lineAndHandle));
        }
    }

    public new void Hide()
    {
        _hoverDelay.Stop();
        _pending = null;
        _dragging = false;
        Root.ReleaseMouseCapture();
        base.Hide();
    }

    void StartDrag(int index)
    {
        _hoverDelay.Stop();
        _dragging = true;
        _from = _to = index;
        Paint();
        Root.CaptureMouse();
    }

    void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var pt = e.GetPosition(Root);
        int hit = _words.FindIndex(w =>
        {
            double left = Canvas.GetLeft(w.Border), top = Canvas.GetTop(w.Border);
            return pt.X >= left && pt.X <= left + w.Border.Width && pt.Y >= top && pt.Y <= top + w.Border.Height;
        });
        if (hit < 0 || hit == _to) return;
        _to = hit;
        Paint();
    }

    void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Root.ReleaseMouseCapture();
        var picked = _words.Skip(Math.Min(_from, _to)).Take(Math.Abs(_to - _from) + 1).Select(w => w.Word).ToList();
        Selected?.Invoke(string.Join(' ', picked.Select(w => w.Text)), Box.Union(picked.Select(w => w.Box)));
    }

    bool IsSelected(int index) => _from >= 0 && index >= Math.Min(_from, _to) && index <= Math.Max(_from, _to);

    void ClearSelection()
    {
        if (_from < 0) return;
        _from = _to = -1;
        Paint();
    }

    void Paint()
    {
        for (int i = 0; i < _words.Count; i++)
            _words[i].Border.Background = IsSelected(i) ? Picked : Unpicked;
    }

    Border AddTarget(Box box, Box area, double s, string? label, Action onHover)
    {
        var border = new Border
        {
            Width = box.W / s + 4,
            Height = box.H / s + 4,
            Background = label is null ? Unpicked : new SolidColorBrush(Color.FromArgb(180, 20, 20, 20)),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(3),
            Cursor = label is null ? Cursors.IBeam : Cursors.Hand,
        };
        if (label != null)
            border.Child = new TextBlock
            {
                Text = label, Foreground = Brushes.White, FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        border.MouseEnter += (_, _) =>
        {
            if (_dragging) return;
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
        return border;
    }
}
