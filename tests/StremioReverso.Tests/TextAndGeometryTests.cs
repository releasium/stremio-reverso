using StremioReverso.Core;

namespace StremioReverso.Tests;

public class TextAndGeometryTests
{
    [Theory]
    [InlineData("Don't,", "don't")]
    [InlineData("don’t", "don't")]
    [InlineData("well-known.", "well-known")]
    [InlineData("‘Hello’", "hello")]
    [InlineData("-Yeah!", "yeah")]
    [InlineData("I", "i")]
    [InlineData("♪", null)]
    [InlineData("123", null)]
    [InlineData("...", null)]
    public void Normalize(string raw, string? expected) =>
        Assert.Equal(expected, WordNormalizer.Normalize(raw));

    [Theory]
    [InlineData("Hi", true)]
    [InlineData("00:12:31", false)]
    [InlineData("♪ a ♪", false)]
    public void IsMeaningful(string text, bool expected) =>
        Assert.Equal(expected, WordNormalizer.IsMeaningful(text));

    [Fact]
    public void EmSplit()
    {
        var parts = EmText.Split("won't <em>bother</em> you");
        Assert.Equal(new[] { ("won't ", false), ("bother", true), (" you", false) }, parts);
    }

    [Fact]
    public void UnionAndZone()
    {
        var u = Box.Union(new[] { new Box(10, 10, 10, 10), new Box(30, 5, 5, 30) });
        Assert.Equal(new Box(10, 5, 25, 30), u);
        Assert.Equal(new Box(0, 650, 1000, 350), Box.SubtitleZone(new Box(0, 0, 1000, 1000)));
    }

    static OcrLine Line(double y) => new("hello there", new Box(100, y, 200, 30), Array.Empty<OcrWord>());

    [Fact]
    public void LineAtPicksNearestLineWithinPadding()
    {
        var lines = new[] { Line(800), Line(840) };
        Assert.Same(lines[1], HitTest.LineAt(lines, 150, 860));
        Assert.Same(lines[0], HitTest.LineAt(lines, 150, 795)); // 5px above, inside 12px pad
        Assert.Null(HitTest.LineAt(lines, 150, 700));
        Assert.Null(HitTest.LineAt(lines, 20, 815));
    }
}
