using AgHouse.Core.Terminal;

namespace AgHouse.Tests;

public class OscParserTests
{
    private static List<TerminalEvent> Parse(params string[] chunks)
    {
        var p = new OscParser();
        var events = new List<TerminalEvent>();
        foreach (var c in chunks) p.Feed(c, events);
        return events;
    }

    [Fact]
    public void Osc9_PlainText_IsNotification()
    {
        var e = Assert.Single(Parse("hi \x1b]9;Build finished\a more"));
        Assert.Equal(new NotifyEvent(null, "Build finished"), e);
    }

    [Fact]
    public void Osc9_WithStTerminator()
    {
        Assert.Equal(new NotifyEvent(null, "done"), Assert.Single(Parse("\x1b]9;done\x1b\\")));
    }

    [Fact]
    public void Osc9_ProgressIsIgnored()
    {
        Assert.Empty(Parse("\x1b]9;4;1;50\a"));
    }

    [Fact]
    public void Osc9_9_IsCwd()
    {
        Assert.Equal(new CwdEvent(@"C:\Users\me"), Assert.Single(Parse("\x1b]9;9;\"C:\\Users\\me\"\x1b\\")));
    }

    [Fact]
    public void Osc777_TitleAndBody()
    {
        Assert.Equal(new NotifyEvent("Claude", "Needs input; please"), Assert.Single(Parse("\x1b]777;notify;Claude;Needs input; please\a")));
    }

    [Fact]
    public void Osc99_ChunkedTitleAndBody()
    {
        var events = Parse("\x1b]99;i=1:d=0;Hello\x1b\\", "\x1b]99;i=1:p=body;World\x1b\\");
        Assert.Equal(new NotifyEvent("Hello", "World"), Assert.Single(events));
    }

    [Fact]
    public void Osc99_TitleOnly()
    {
        Assert.Equal(new NotifyEvent(null, "Hi"), Assert.Single(Parse("\x1b]99;;Hi\x1b\\")));
    }

    [Fact]
    public void Osc7_FileUrl_IsWindowsPath()
    {
        Assert.Equal(new CwdEvent(@"C:\Users\me x"), Assert.Single(Parse("\x1b]7;file://HOST/C:/Users/me%20x\a")));
    }

    [Fact]
    public void Osc7_PosixPathUnchanged()
    {
        Assert.Equal(new CwdEvent("/home/me"), Assert.Single(Parse("\x1b]7;file://wsl/home/me\x1b\\")));
    }

    [Fact]
    public void Title()
    {
        Assert.Equal(new TitleEvent("vim"), Assert.Single(Parse("\x1b]0;vim\a")));
    }

    [Fact]
    public void BareBell_IsBell_ButOscTerminatorIsNot()
    {
        var events = Parse("a\ab\x1b]2;t\a");
        Assert.Equal(2, events.Count);
        Assert.IsType<BellEvent>(events[0]);
        Assert.IsType<TitleEvent>(events[1]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public void SequenceSplitAcrossChunks(int chunkSize)
    {
        const string s = "xx\x1b]777;notify;T;B\x1b\\yy\x1b]9;9;C:\\a\ayy";
        var chunks = s.Chunk(chunkSize).Select(c => new string(c)).ToArray();
        var events = Parse(chunks);
        Assert.Equal([new NotifyEvent("T", "B"), new CwdEvent(@"C:\a")], events);
    }

    [Fact]
    public void BellInsideDcs_IsIgnored()
    {
        Assert.Empty(Parse("\x1bPq\a#0\x1b\\"));
    }

    [Fact]
    public void AbortedOsc_DoesNotSwallowFollowingSequence()
    {
        var events = Parse("\x1b]9;partial\x1b[0m\x1b]9;real\a");
        Assert.Equal(new NotifyEvent(null, "real"), Assert.Single(events));
    }

    [Fact]
    public void CsiAndPlainText_ProduceNothing()
    {
        Assert.Empty(Parse("\x1b[31mred\x1b[0m\r\n\x1b[?25h"));
    }
}
