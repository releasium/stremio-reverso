namespace StremioReverso.Core;

public static class HitTest
{
    /// The OCR line under (or within pad px of) the point; the vertically nearest one wins.
    public static OcrLine? LineAt(IReadOnlyList<OcrLine> lines, double x, double y, double pad = 12) =>
        lines.Where(l => l.Box.Contains(x, y, pad))
             .OrderBy(l => Math.Abs(l.Box.Y + l.Box.H / 2 - y))
             .FirstOrDefault();
}
