using AgHouse.Core.Terminal;

namespace AgHouse.Tests;

public class OutputMirrorTests
{
    private static OutputMirror Fed(params string[] chunks)
    {
        var m = new OutputMirror();
        foreach (var c in chunks) m.Feed(c);
        return m;
    }

    [Fact]
    public void Lines_SplitOnNewline()
    {
        Assert.Equal(["one", "two", "three"], Fed("one\r\ntwo\r\nthree").Lines());
    }

    [Fact]
    public void Colours_AndOsc_AreStripped()
    {
        var m = Fed("\x1b[31mred\x1b[0m text\x1b]0;title\a\r\n\x1b]9;9;C:\\\x1b\\after\r\n");
        Assert.Equal(["red text", "after"], m.Lines());
    }

    [Fact]
    public void CursorForward_BecomesSpaces()
    {
        Assert.Equal(["a   b"], Fed("a\x1b[3Cb\r\n").Lines());
    }

    [Fact]
    public void CursorJump_EndsTheLine()
    {
        Assert.Equal(["top", "next"], Fed("top\x1b[2;1Hnext").Lines());
    }

    [Fact]
    public void BareCarriageReturn_RedrawsTheLine()
    {
        Assert.Equal(["100%"], Fed("10%\r50%\r100%\r\n").Lines());
    }

    [Fact]
    public void SequencesSplitAcrossChunks()
    {
        Assert.Equal(["ok"], Fed("\x1b[3", "1mok\x1b", "[0m\r", "\n").Lines());
    }

    [Fact]
    public void OldLinesAreDropped()
    {
        var m = new OutputMirror(maxLines: 2);
        m.Feed("a\nb\nc\n");
        Assert.Equal(["b", "c"], m.Lines());
    }

    [Fact]
    public void Find_NewestFirst_FoldsRepeats_IgnoresCase()
    {
        var m = Fed("error: one\r\nfine\r\nERROR: two\r\nerror: one\r\n");
        var found = m.Find("error");
        Assert.Equal(2, found.Count);
        Assert.Equal(new FindMatch("error: one", 2), found[0]);
        Assert.Equal(new FindMatch("ERROR: two", 1), found[1]);
    }

    [Fact]
    public void Find_EmptyQuery_FindsNothing()
    {
        Assert.Empty(Fed("x\n").Find("  "));
    }
}
