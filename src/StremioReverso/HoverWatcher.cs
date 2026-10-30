using System.Windows.Threading;
using StremioReverso.Core;
using StremioReverso.Ocr;
using StremioReverso.Stremio;
using StremioReverso.UI;
using StremioReverso.Win32;

namespace StremioReverso;

/// Glues the OS to HoverLogic: polls cursor/window 30×/s, keeps a background OCR of the
/// subtitle zone, and carries out the actions HoverLogic returns.
internal sealed class HoverWatcher
{
    readonly StremioWindow _stremio = new();
    readonly SubtitleOcr _ocr = new();
    readonly ReversoClient _reverso = new(ReversoClient.CreateHttpClient());
    readonly HoverLogic _logic = new();
    readonly WordOverlay _overlay = new();
    readonly TranslationPopup _popup = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    static readonly TimeSpan ScanInterval = TimeSpan.FromMilliseconds(250);
    static readonly TimeSpan ScanFreshness = TimeSpan.FromMilliseconds(700);
    IReadOnlyList<OcrLine> _lines = Array.Empty<OcrLine>();
    DateTime _lastScan;
    bool _scanning, _freezing;
    DateTime _ignoreInputUntil;
    CancellationTokenSource? _lookup;
    string _status = "";

    public event Action<string>? StatusChanged;

    public HoverWatcher()
    {
        _timer.Tick += (_, _) =>
        {
            try { Tick(); }
            catch (Exception e) { Log.Error("tick", e); }
        };
        _overlay.WordHovered += OnWordHovered;
        _overlay.LineHovered += OnLineHovered;
        _popup.PinnedChanged += pinned => _logic.Pinned = pinned;
    }

    public void Start() => _timer.Start();

    void Tick()
    {
        var now = DateTime.UtcNow;
        Native.GetCursorPos(out var p);
        var bounds = _stremio.Bounds;
        SetStatus(bounds is null ? "Stremio Reverso: waiting for Stremio" : "Stremio Reverso: hover subtitles");

        bool foreground = bounds != null && _stremio.IsForeground;
        bool overUi = WindowPlacement.ContainsCursor(_overlay, p.X, p.Y) || WindowPlacement.ContainsCursor(_popup, p.X, p.Y);
        bool inZone = overUi || (foreground && Box.SubtitleZone(bounds!.Value).Contains(p.X, p.Y));

        if (_logic.IsPaused && !overUi && now > _ignoreInputUntil &&
            (Native.IsDown(Native.VK_LBUTTON) || Native.IsDown(Native.VK_RBUTTON) || Native.IsDown(Native.VK_SPACE)))
            _logic.OnUserInput();

        // Keep a fresh OCR of the subtitle zone while Stremio plays, so hovering a line pauses at once.
        if (foreground && !_logic.IsPaused && !_scanning && now - _lastScan >= ScanInterval)
            _ = ScanAsync(Box.SubtitleZone(bounds!.Value));
        var lines = now - _lastScan <= ScanFreshness ? _lines : Array.Empty<OcrLine>();

        Act(_logic.OnTick(now, inZone, foreground, p.X, p.Y, lines));
        if (_logic.IsPaused && !_overlay.IsVisible && !_freezing && bounds is { } b)
            _ = ShowOverlayAsync(Box.SubtitleZone(b));
    }

    void Act(HoverAction action)
    {
        switch (action)
        {
            case HoverAction.Pause:
                TogglePlayback();
                break;
            case HoverAction.Resume:
                HideUi();
                TogglePlayback();
                break;
            case HoverAction.Dismiss:
                HideUi();
                break;
        }
    }

    async Task ScanAsync(Box zone)
    {
        _scanning = true;
        try
        {
            _lines = await _ocr.RecognizeAsync(zone);
            _lastScan = DateTime.UtcNow;
        }
        catch (Exception e) { Log.Error("scan", e); }
        finally { _scanning = false; }
    }

    async Task ShowOverlayAsync(Box zone)
    {
        _freezing = true;
        try
        {
            await Task.Delay(120); // let the paused frame settle, then read it cleanly
            var frozen = await _ocr.RecognizeAsync(zone);
            if (!_logic.IsPaused) return;
            Act(_logic.OnFrozenFrame(frozen));
            if (_logic.IsPaused) _overlay.ShowLines(frozen);
        }
        catch (Exception e) { Log.Error("freeze", e); }
        finally { _freezing = false; }
    }

    async void OnWordHovered(OcrLine line, OcrWord word)
    {
        try
        {
            var query = WordNormalizer.Normalize(word.Text);
            if (query is null) return;
            var ct = NewLookup();
            _popup.ShowLoading(query, word.Box);
            var result = await _reverso.GetWordAsync(query, ct);
            if (!ct.IsCancellationRequested) _popup.ShowWord(query, result, word.Box);
        }
        catch (Exception e) { Log.Error("word", e); }
    }

    async void OnLineHovered(OcrLine line)
    {
        try
        {
            var ct = NewLookup();
            _popup.ShowLoading(line.Text, line.Box);
            var result = await _reverso.TranslateLineAsync(line.Text, ct);
            if (!ct.IsCancellationRequested) _popup.ShowLine(line.Text, result, line.Box);
        }
        catch (Exception e) { Log.Error("line", e); }
    }

    CancellationToken NewLookup()
    {
        _lookup?.Cancel();
        _lookup = new CancellationTokenSource();
        return _lookup.Token;
    }

    void HideUi()
    {
        _lookup?.Cancel();
        _overlay.Hide();
        _popup.HidePopup();
    }

    void TogglePlayback()
    {
        _ignoreInputUntil = DateTime.UtcNow.AddMilliseconds(300);
        Playback.TogglePlayPause();
    }

    void SetStatus(string status)
    {
        if (status == _status) return;
        _status = status;
        StatusChanged?.Invoke(status);
    }
}
