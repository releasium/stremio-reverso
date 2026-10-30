using StremioReverso.Core;

namespace StremioReverso.Tests;

public class HoverLogicTests
{
    static readonly DateTime T0 = new(2026, 1, 1);
    static DateTime Ms(int ms) => T0.AddMilliseconds(ms);
    static readonly OcrLine[] Lines = { new("hello there", new Box(100, 800, 200, 30), Array.Empty<OcrWord>()) };
    static readonly OcrLine[] None = Array.Empty<OcrLine>();

    static HoverLogic Paused()
    {
        var h = new HoverLogic();
        Assert.Equal(HoverAction.Pause, h.OnTick(Ms(400), true, true, 150, 815, Lines));
        return h;
    }

    [Fact]
    public void PausesOnFirstTickOverAKnownLine()
    {
        var h = new HoverLogic();
        Assert.Equal(HoverAction.Pause, h.OnTick(Ms(0), true, true, 150, 815, Lines));
        Assert.True(h.IsPaused && h.PausedByUs);
    }

    [Fact]
    public void InZoneButOffTheTextDoesNothing()
    {
        var h = new HoverLogic();
        Assert.Equal(HoverAction.None, h.OnTick(Ms(0), true, true, 150, 700, Lines));
        Assert.Equal(HoverAction.None, h.OnTick(Ms(0), true, true, 150, 815, None));
    }

    [Fact]
    public void OutOfZoneDoesNothing() =>
        Assert.Equal(HoverAction.None, new HoverLogic().OnTick(Ms(0), false, true, 150, 815, Lines));

    [Fact]
    public void PauseThenLeaveResumesAfterGrace()
    {
        var h = Paused();
        Assert.Equal(HoverAction.None, h.OnTick(Ms(500), true, true, 150, 815, Lines));
        Assert.Equal(HoverAction.None, h.OnTick(Ms(600), false, true, 150, 100, None));
        Assert.Equal(HoverAction.None, h.OnTick(Ms(800), false, true, 150, 100, None));
        Assert.Equal(HoverAction.Resume, h.OnTick(Ms(910), false, true, 150, 100, None));
        Assert.False(h.IsPaused);
    }

    [Fact]
    public void ComingBackWithinGraceStaysPaused()
    {
        var h = Paused();
        h.OnTick(Ms(600), false, true, 150, 100, None);
        h.OnTick(Ms(700), true, true, 150, 815, None);
        Assert.Equal(HoverAction.None, h.OnTick(Ms(1000), false, true, 150, 100, None));
        Assert.Equal(HoverAction.None, h.OnTick(Ms(1200), false, true, 150, 100, None));
        Assert.Equal(HoverAction.Resume, h.OnTick(Ms(1300), false, true, 150, 100, None));
    }

    [Fact]
    public void PinnedNeverResumes()
    {
        var h = Paused();
        h.Pinned = true;
        h.OnTick(Ms(600), false, true, 150, 100, None);
        Assert.Equal(HoverAction.None, h.OnTick(Ms(2000), false, true, 150, 100, None));
        h.Pinned = false;
        h.OnTick(Ms(2100), false, true, 150, 100, None);
        Assert.Equal(HoverAction.Resume, h.OnTick(Ms(2500), false, true, 150, 100, None));
    }

    [Fact]
    public void UserInputMeansDismissNotResume()
    {
        var h = Paused();
        h.OnUserInput();
        h.OnTick(Ms(600), false, true, 150, 100, None);
        Assert.Equal(HoverAction.Dismiss, h.OnTick(Ms(1000), false, true, 150, 100, None));
    }

    [Fact]
    public void StremioLostFocusMeansDismiss()
    {
        var h = Paused();
        h.OnTick(Ms(600), false, false, 150, 100, None);
        Assert.Equal(HoverAction.Dismiss, h.OnTick(Ms(1000), false, false, 150, 100, None));
    }

    [Fact]
    public void FrozenFrameWithoutTextResumes()
    {
        var h = Paused();
        Assert.Equal(HoverAction.Resume, h.OnFrozenFrame(None));
        Assert.False(h.IsPaused);
        Assert.Equal(HoverAction.None, Paused().OnFrozenFrame(Lines));
    }
}
