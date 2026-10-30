namespace StremioReverso.Core;

public enum HoverAction { None, StartOcr, Pause, Resume, Dismiss }

/// Decides when to OCR, pause, and resume. Pure: the caller feeds it time, cursor and window state.
public sealed class HoverLogic
{
    public static readonly TimeSpan Dwell = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan LeaveGrace = TimeSpan.FromMilliseconds(300);
    public const double StillTolerance = 4;

    enum Phase { Idle, Dwelling, Recognizing, Paused }

    Phase _phase = Phase.Idle;
    double _lastX, _lastY;
    DateTime _stillSince, _cooldownUntil, _leftAt;
    bool _left;

    public bool PausedByUs { get; private set; }
    public bool Pinned { get; set; }
    public bool IsPaused => _phase == Phase.Paused;

    /// inZone: the cursor is over our overlay/popup, or Stremio is foreground and the cursor is in its subtitle zone.
    public HoverAction OnTick(DateTime now, bool inZone, bool stremioForeground, double x, double y)
    {
        switch (_phase)
        {
            case Phase.Paused:
                if (inZone || Pinned) { _left = false; return HoverAction.None; }
                if (!_left) { _left = true; _leftAt = now; }
                if (now - _leftAt < LeaveGrace) return HoverAction.None;
                var resume = PausedByUs && stremioForeground;
                Reset(now, cooldown: false);
                return resume ? HoverAction.Resume : HoverAction.Dismiss;

            case Phase.Recognizing:
                return HoverAction.None;

            default:
                if (!inZone || now < _cooldownUntil) { _phase = Phase.Idle; return HoverAction.None; }
                if (_phase == Phase.Idle || Math.Abs(x - _lastX) > StillTolerance || Math.Abs(y - _lastY) > StillTolerance)
                {
                    _phase = Phase.Dwelling;
                    _stillSince = now;
                    _lastX = x;
                    _lastY = y;
                    return HoverAction.None;
                }
                if (now - _stillSince < Dwell) return HoverAction.None;
                _phase = Phase.Recognizing;
                return HoverAction.StartOcr;
        }
    }

    public HoverAction OnOcrResult(DateTime now, IReadOnlyList<OcrLine> lines, double x, double y)
    {
        if (_phase != Phase.Recognizing) return HoverAction.None;
        if (HitTest.LineAt(lines, x, y) is null) { Reset(now, cooldown: true); return HoverAction.None; }
        _phase = Phase.Paused;
        PausedByUs = true;
        _left = false;
        return HoverAction.Pause;
    }

    public void OnOcrFailed(DateTime now)
    {
        if (_phase == Phase.Recognizing) Reset(now, cooldown: true);
    }

    /// The user clicked or pressed a key in Stremio while we were paused, so playback is theirs again.
    public void OnUserInput()
    {
        if (_phase == Phase.Paused) PausedByUs = false;
    }

    void Reset(DateTime now, bool cooldown)
    {
        _phase = Phase.Idle;
        PausedByUs = false;
        Pinned = false;
        _left = false;
        if (cooldown) _cooldownUntil = now + Cooldown;
    }
}
