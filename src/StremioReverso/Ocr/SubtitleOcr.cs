using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using StremioReverso.Core;
using WinOcr = Windows.Media.Ocr;

namespace StremioReverso.Ocr;

internal sealed class SubtitleOcr
{
    readonly WinOcr.OcrEngine _engine = WinOcr.OcrEngine.TryCreateFromLanguage(new Language("en-US"))
        ?? throw new InvalidOperationException("English (en-US) OCR is not installed");

    /// OCRs a screen rectangle (physical pixels) and returns subtitle-looking lines in screen coordinates.
    public async Task<IReadOnlyList<OcrLine>> RecognizeAsync(Box region)
    {
        int w = (int)region.W, h = (int)region.H;
        if (w <= 0 || h <= 0) return Array.Empty<OcrLine>();
        double scale = Math.Min(2.0, WinOcr.OcrEngine.MaxImageDimension / (double)Math.Max(w, h));
        int sw = (int)(w * scale), sh = (int)(h * scale);

        using var shot = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(shot))
            g.CopyFromScreen((int)region.X, (int)region.Y, 0, 0, new Size(w, h));
        using var scaled = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(shot, 0, 0, sw, sh);
        }

        using var ms = new MemoryStream();
        scaled.Save(ms, ImageFormat.Bmp);
        ms.Position = 0;
        var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await _engine.RecognizeAsync(bitmap);
        return result.Lines
            .Select(l => ToLine(l, region, scale))
            .Where(l => WordNormalizer.IsMeaningful(l.Text))
            .ToList();
    }

    static OcrLine ToLine(WinOcr.OcrLine line, Box region, double scale)
    {
        var words = line.Words.Select(w => new OcrWord(w.Text, new Box(
            region.X + w.BoundingRect.X / scale,
            region.Y + w.BoundingRect.Y / scale,
            w.BoundingRect.Width / scale,
            w.BoundingRect.Height / scale))).ToList();
        return new OcrLine(line.Text, Box.Union(words.Select(w => w.Box)), words);
    }
}
