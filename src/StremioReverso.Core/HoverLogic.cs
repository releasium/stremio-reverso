namespace StremioReverso.Core;

public enum HoverAction { None, Pause, Resume, Dismiss }

/// Decides when to pause and resume. Pure: the caller feeds it time, cursor, window state,
/// and the latest background OCR of the subtitle zone.
public sealed class HoverLogic
{
    public static readonly TimeSpan LeaveGrace = TimeSpan.FromMilliseconds(300);

    bool _paused, _left;
    DateTime _leftAt;

    public bool PausedByUs { get; private set; }
    public bool Pinned { get; set; }
    public bool IsPaused => _paused;

    /// inZone: the cursor is over our overlay/popup, or Stremio is foreground and the cursor is in its subtitle zone.
    /// lines: the most recent OCR of the subtitle zone (empty when stale or unknown).
    public HoverAction OnTick(DateTime now, bool inZone, bool stremioForeground, double x, double y, IReadOnlyList<OcrLine> lines)
    {
        if (!_paused)
        {
            if (!inZone || HitTest.LineAt(lines, x, y) is null) return HoverAction.None;
            _paused = true;
            PausedByUs = true;
            _left = false;
            return HoverAction.Pause;
        }

        if (inZone || Pinned) { _left = false; return HoverAction.None; }
        if (!_left) { _left = true; _leftAt = now; }
        if (now - _leftAt < LeaveGrace) return HoverAction.None;
        var resume = PausedByUs && stremioForeground;
        Reset();
        return resume ? HoverAction.Resume : HoverAction.Dismiss;
    }

    /// OCR of the frame after pausing. No text means the cached lines were stale: undo the pause.
    public HoverAction OnFrozenFrame(IReadOnlyList<OcrLine> lines)
    {
        if (!_paused || lines.Count > 0) return HoverAction.None;
        var resume = PausedByUs;
        Reset();
        return resume ? HoverAction.Resume : HoverAction.Dismiss;
    }

    /// The user clicked or pressed a key in Stremio while we were paused, so playback is theirs again.
    public void OnUserInput()
    {
        if (_paused) PausedByUs = false;
    }

    void Reset()
    {
        _paused = false;
        PausedByUs = false;
        Pinned = false;
        _left = false;
    }
}
