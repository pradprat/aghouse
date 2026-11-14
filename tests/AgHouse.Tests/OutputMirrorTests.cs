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

    [Fact]
    public void Read_LastLines_AndCursorCountsFinishedLines()
    {
        var read = Fed("a\r\nb\r\nc\r\n").Read(last: 2);
        Assert.Equal(["b", "c"], read.Lines);
        Assert.Equal(3, read.Next);
        Assert.Null(read.Partial);
        Assert.False(read.Truncated);
    }

    [Fact]
    public void Read_Since_ReturnsOnlyWhatCameAfter()
    {
        var m = Fed("old\r\n");
        var cursor = m.Read(last: 0).Next;
        m.Feed("new 1\r\nnew 2\r\n");
        var read = m.Read(since: cursor);
        Assert.Equal(["new 1", "new 2"], read.Lines);
        Assert.Equal(3, read.Next);
        Assert.Empty(m.Read(since: read.Next).Lines);
    }

    [Fact]
    public void Read_UnfinishedLine_ComesSeparately_AndDoesNotMoveTheCursor()
    {
        var m = Fed("done\r\nPS C:\\> ");
        var read = m.Read();
        Assert.Equal(["done"], read.Lines);
        Assert.Equal("PS C:\\>", read.Partial);
        Assert.Equal(1, read.Next);
    }

    [Fact]
    public void Read_LastZero_GivesOnlyTheCursor()
    {
        var read = Fed("a\nb\n").Read(last: 0);
        Assert.Empty(read.Lines);
        Assert.Equal(2, read.Next);
    }

    [Fact]
    public void Read_CursorKeepsCountingPastDroppedLines()
    {
        var m = new OutputMirror(maxLines: 2);
        m.Feed("a\nb\nc\nd\n");
        Assert.Equal(4, m.Read().Next);
        var read = m.Read(since: 1); // b was dropped
        Assert.True(read.Truncated);
        Assert.Equal(["c", "d"], read.Lines);
        Assert.False(m.Read(since: 2).Truncated);
        Assert.Equal(["d"], m.Read(since: 3).Lines);
    }

    [Fact]
    public void Read_CursorFromTheFuture_GivesNothing()
    {
        var read = Fed("a\n").Read(since: 99);
        Assert.Empty(read.Lines);
        Assert.Equal(1, read.Next);
    }
}
