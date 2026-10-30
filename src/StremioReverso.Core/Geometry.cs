namespace StremioReverso.Core;

/// Rectangle in screen pixels.
public readonly record struct Box(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;

    public bool Contains(double px, double py, double pad = 0) =>
        px >= X - pad && px <= Right + pad && py >= Y - pad && py <= Bottom + pad;

    public static Box Union(IEnumerable<Box> boxes)
    {
        var list = boxes.ToList();
        if (list.Count == 0) return default;
        double l = list.Min(b => b.X), t = list.Min(b => b.Y);
        double r = list.Max(b => b.Right), btm = list.Max(b => b.Bottom);
        return new Box(l, t, r - l, btm - t);
    }

    /// The bottom part of a window, where subtitles are drawn.
    public static Box SubtitleZone(Box window, double fraction = 0.35) =>
        new(window.X, window.Bottom - window.H * fraction, window.W, window.H * fraction);
}

public sealed record OcrWord(string Text, Box Box);

public sealed record OcrLine(string Text, Box Box, IReadOnlyList<OcrWord> Words);
