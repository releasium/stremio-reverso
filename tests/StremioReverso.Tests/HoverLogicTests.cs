using StremioReverso.Core;

namespace StremioReverso.Tests;

public class HoverLogicTests
{
    static readonly DateTime T0 = new(2026, 1, 1);
    static DateTime Ms(int ms) => T0.AddMilliseconds(ms);
    static readonly OcrLine[] Lines = { new("hello there", new Box(100, 800, 200, 30), Array.Empty<OcrWord>()) };

    static HoverLogic Paused()
    {
        var h = new HoverLogic();
        h.OnTick(Ms(0), true, true, 150, 815);
        Assert.Equal(HoverAction.StartOcr, h.OnTick(Ms(260), true, true, 150, 815));
        Assert.Equal(HoverAction.Pause, h.OnOcrResult(Ms(400), Lines, 150, 815));
        return h;
    }

    [Fact]
    public void DwellStartsOcr()
    {
        var h = new HoverLogic();
        Assert.Equal(HoverAction.None, h.OnTick(Ms(0), true, true, 150, 815));
        Assert.Equal(HoverAction.None, h.OnTick(Ms(100), true, true, 151, 815));
        Assert.Equal(HoverAction.StartOcr, h.OnTick(Ms(260), true, true, 152, 816));
        Assert.Equal(HoverAction.None, h.OnTick(Ms(300), true, true, 152, 816)); // waiting for OCR
    }

    [Fact]
    public void MovingRestartsDwell()
    {
        var h = new HoverLogic();
        h.OnTick(Ms(0), true, true, 150, 815);
        h.OnTick(Ms(200), true, true, 180, 815);
        Assert.Equal(HoverAction.None, h.OnTick(Ms(300), true, true, 180, 815));
        Assert.Equal(HoverAction.StartOcr, h.OnTick(Ms(460), true, true, 180, 815));
    }

    [Fact]
    public void OutOfZoneDoesNothing()
    {
        var h = new HoverLogic();
        h.OnTick(Ms(0), false, true, 150, 100);
        Assert.Equal(HoverAction.None, h.OnTick(Ms(500), false, true, 150, 100));
    }

    [Fact]
    public void MissThenCooldown()
    {
        var h = new HoverLogic();
        h.OnTick(Ms(0), true, true, 150, 700);
        h.OnTick(Ms(260), true, true, 150, 700);
        Assert.Equal(HoverAction.None, h.OnOcrResult(Ms(400), Lines, 150, 700));
        Assert.False(h.IsPaused);
        h.OnTick(Ms(500), true, true, 150, 700);
        Assert.Equal(HoverAction.None, h.OnTick(Ms(1300), true, true, 150, 700)); // still cooling down until 1400
        h.OnTick(Ms(1450), true, true, 150, 700);
        Assert.Equal(HoverAction.StartOcr, h.OnTick(Ms(1710), true, true, 150, 700));
    }

    [Fact]
    public void OcrFailureReturnsToIdle()
    {
        var h = new HoverLogic();
        h.OnTick(Ms(0), true, true, 150, 815);
        h.OnTick(Ms(260), true, true, 150, 815);
        h.OnOcrFailed(Ms(300));
        Assert.Equal(HoverAction.None, h.OnOcrResult(Ms(310), Lines, 150, 815));
    }

    [Fact]
    public void PauseThenLeaveResumesAfterGrace()
    {
        var h = Paused();
        Assert.True(h.IsPaused && h.PausedByUs);
        Assert.Equal(HoverAction.None, h.OnTick(Ms(500), true, true, 150, 815));
        Assert.Equal(HoverAction.None, h.OnTick(Ms(600), false, true, 150, 100));
        Assert.Equal(HoverAction.None, h.OnTick(Ms(800), false, true, 150, 100));
        Assert.Equal(HoverAction.Resume, h.OnTick(Ms(910), false, true, 150, 100));
        Assert.False(h.IsPaused);
    }

    [Fact]
    public void ComingBackWithinGraceStaysPaused()
    {
        var h = Paused();
        h.OnTick(Ms(600), false, true, 150, 100);
        h.OnTick(Ms(700), true, true, 150, 815);
        Assert.Equal(HoverAction.None, h.OnTick(Ms(1000), false, true, 150, 100));
        Assert.Equal(HoverAction.None, h.OnTick(Ms(1200), false, true, 150, 100));
        Assert.Equal(HoverAction.Resume, h.OnTick(Ms(1300), false, true, 150, 100));
    }

    [Fact]
    public void PinnedNeverResumes()
    {
        var h = Paused();
        h.Pinned = true;
        h.OnTick(Ms(600), false, true, 150, 100);
        Assert.Equal(HoverAction.None, h.OnTick(Ms(2000), false, true, 150, 100));
        h.Pinned = false;
        h.OnTick(Ms(2100), false, true, 150, 100);
        Assert.Equal(HoverAction.Resume, h.OnTick(Ms(2500), false, true, 150, 100));
    }

    [Fact]
    public void UserInputMeansDismissNotResume()
    {
        var h = Paused();
        h.OnUserInput();
        h.OnTick(Ms(600), false, true, 150, 100);
        Assert.Equal(HoverAction.Dismiss, h.OnTick(Ms(1000), false, true, 150, 100));
    }

    [Fact]
    public void StremioLostFocusMeansDismiss()
    {
        var h = Paused();
        h.OnTick(Ms(600), false, false, 150, 100);
        Assert.Equal(HoverAction.Dismiss, h.OnTick(Ms(1000), false, false, 150, 100));
    }
}
