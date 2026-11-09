using AgHouse.Core.Terminal;

namespace AgHouse.Tests;

// Attention is what the app is for: every way a pane can ask for the user starts as one of these escape
// sequences. Sequences use \u escapes because "\x9d9" would be read by C# as the single char U+9D9.
public class AttentionParsingTests
{
    private const string Esc = "\u001b";
    private const string St = Esc + "\\";

    private static List<TerminalEvent> Parse(params string[] chunks)
    {
        var p = new OscParser();
        var events = new List<TerminalEvent>();
        foreach (var c in chunks) p.Feed(c, events);
        return events;
    }

    [Fact]
    public void Osc9_Empty_IsIgnored()
    {
        Assert.Empty(Parse($"{Esc}]9;\a"));
    }

    [Fact]
    public void Osc9_Cwd_EmptyPath_IsIgnored()
    {
        Assert.Empty(Parse($"{Esc}]9;9;\"\"{St}"));
    }

    [Fact]
    public void Osc9_Text_ThatStartsWithADigit_ButIsNotAConEmuCommand_IsANotification()
    {
        Assert.Equal(new NotifyEvent(null, "3 tests failed"), Assert.Single(Parse($"{Esc}]9;3 tests failed\a")));
    }

    [Theory]
    [InlineData("1;5")]   // ConEmu sleep
    [InlineData("4;3")]   // progress
    [InlineData("12")]    // bare ConEmu command number
    public void Osc9_ConEmuCommands_AreNotNotifications(string payload)
    {
        Assert.Empty(Parse($"{Esc}]9;{payload}\a"));
    }

    [Fact]
    public void Osc777_NotNotify_IsIgnored()
    {
        Assert.Empty(Parse($"{Esc}]777;precmd\a"));
        Assert.Empty(Parse($"{Esc}]777;preexec;ls\a"));
    }

    [Fact]
    public void Osc777_TitleOnly_HasEmptyBody()
    {
        Assert.Equal(new NotifyEvent("Codex", ""), Assert.Single(Parse($"{Esc}]777;notify;Codex\a")));
    }

    [Fact]
    public void Osc777_EmptyTitle_BecomesNull()
    {
        Assert.Equal(new NotifyEvent(null, "body"), Assert.Single(Parse($"{Esc}]777;notify;;body\a")));
    }

    [Fact]
    public void Osc99_Base64Payload_IsDecoded()
    {
        var payload = Convert.ToBase64String("Waiting for approval ✓"u8.ToArray());
        Assert.Equal(new NotifyEvent(null, "Waiting for approval ✓"), Assert.Single(Parse($"{Esc}]99;e=1;{payload}{St}")));
    }

    [Fact]
    public void Osc99_InvalidBase64_IsIgnored()
    {
        Assert.Empty(Parse($"{Esc}]99;e=1;***not-base64***{St}"));
    }

    [Fact]
    public void Osc99_UnfinishedChunk_EmitsNothingUntilDone()
    {
        Assert.Empty(Parse($"{Esc}]99;i=7:d=0;Half{St}"));
    }

    [Fact]
    public void Osc99_InterleavedIds_StayIndependent()
    {
        var events = Parse(
            $"{Esc}]99;i=a:d=0;TitleA{St}",
            $"{Esc}]99;i=b:d=0;TitleB{St}",
            $"{Esc}]99;i=b:p=body;BodyB{St}",
            $"{Esc}]99;i=a:p=body;BodyA{St}");

        Assert.Equal([new NotifyEvent("TitleB", "BodyB"), new NotifyEvent("TitleA", "BodyA")], events);
    }

    [Fact]
    public void Osc99_UnknownPart_IsIgnored()
    {
        Assert.Empty(Parse($"{Esc}]99;i=1:p=?;{St}"));
    }

    [Fact]
    public void Osc99_WithoutPayloadSeparator_IsIgnored()
    {
        Assert.Empty(Parse($"{Esc}]99{St}"));
    }

    [Fact]
    public void C1Controls_AlsoDelimitOsc()
    {
        Assert.Equal(new NotifyEvent("T", "B"), Assert.Single(Parse("\u009d777;notify;T;B\u009c")));
    }

    [Fact]
    public void BellAfterAStringTerminatedByC1St_IsABell()
    {
        var events = Parse($"{Esc}Pq#0\u009c\a");
        Assert.IsType<BellEvent>(Assert.Single(events));
    }

    [Fact]
    public void DoubleEscape_BeforeOsc_StillStartsTheOsc()
    {
        Assert.Equal(new NotifyEvent(null, "x"), Assert.Single(Parse($"{Esc}{Esc}]9;x\a")));
    }

    [Fact]
    public void EmptyTitle_IsReported()
    {
        Assert.Equal(new TitleEvent(""), Assert.Single(Parse($"{Esc}]2;\a")));
    }

    [Fact]
    public void Osc7_NonFileUrl_IsIgnored()
    {
        Assert.Empty(Parse($"{Esc}]7;https://example.com/a\a"));
    }

    [Fact]
    public void ManyEvents_InOneChunk_KeepTheirOrder()
    {
        var events = Parse($"\a{Esc}]2;t\a{Esc}]9;one\a{Esc}]777;notify;two;2\a\a");
        Assert.Collection(events,
            e => Assert.IsType<BellEvent>(e),
            e => Assert.Equal(new TitleEvent("t"), e),
            e => Assert.Equal(new NotifyEvent(null, "one"), e),
            e => Assert.Equal(new NotifyEvent("two", "2"), e),
            e => Assert.IsType<BellEvent>(e));
    }

    [Fact]
    public void OscExactlyAtTheLengthLimit_IsDelivered_OneOverIsDropped()
    {
        // The limit is 16 KiB of OSC text, counting the "9;" prefix.
        var atLimit = new string('a', 16 * 1024 - 2);
        var overLimit = new string('a', 16 * 1024 - 1);

        var kept = Assert.Single(Parse($"{Esc}]9;{atLimit}\a"));
        Assert.Equal(atLimit, Assert.IsType<NotifyEvent>(kept).Body);
        Assert.Empty(Parse($"{Esc}]9;{overLimit}\a"));
    }

    [Fact]
    public void AfterAnOversizedOsc_TheParserRecovers()
    {
        var events = Parse($"{Esc}]9;{new string('x', 40_000)}\a{Esc}]9;ok\a");
        Assert.Equal(new NotifyEvent(null, "ok"), Assert.Single(events));
    }

    [Fact]
    public void OneParser_KeepsWorking_AcrossManyFeeds()
    {
        var parser = new OscParser();
        var events = new List<TerminalEvent>();
        for (var i = 0; i < 100; i++) parser.Feed($"line {i}\r\n{Esc}]9;n{i}\a", events);

        Assert.Equal(100, events.Count);
        Assert.Equal(new NotifyEvent(null, "n99"), events[^1]);
    }

    [Fact]
    public void UnterminatedOsc_ProducesNothing_AndNextFeedCanCompleteIt()
    {
        var parser = new OscParser();
        var events = new List<TerminalEvent>();
        parser.Feed($"{Esc}]9;par", events);
        Assert.Empty(events);
        parser.Feed("tial\a", events);
        Assert.Equal(new NotifyEvent(null, "partial"), Assert.Single(events));
    }
}

public class CwdUrlTests
{
    [Theory]
    [InlineData("file:///C:/Users/me", @"C:\Users\me")]            // empty host
    [InlineData("file://HOST/C:/Users/me%20x", @"C:\Users\me x")]  // host and percent-escapes
    [InlineData("FILE://h/c:/x", @"c:\x")]                          // scheme is case-insensitive
    [InlineData("file://h/C:", @"C:\")]                             // bare drive gets a root
    [InlineData("file://wsl/home/me", "/home/me")]                  // POSIX path unchanged
    [InlineData("file://h/", "/")]
    [InlineData("file://h/caf%C3%A9", "/café")]                     // UTF-8 escapes
    public void TryParse_Valid(string url, string expected)
    {
        Assert.True(CwdUrl.TryParse(url, out var path));
        Assert.Equal(expected, path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("C:\\plain\\path")]
    [InlineData("http://example.com/a")]
    [InlineData("file://hostonly")]
    [InlineData("file:/one/slash")]
    public void TryParse_Invalid(string url)
    {
        Assert.False(CwdUrl.TryParse(url, out var path));
        Assert.Equal("", path);
    }

    [Theory]
    [InlineData("C:/x/y", @"C:\x\y")]
    [InlineData("/C:/x", @"C:\x")]
    [InlineData("d:", @"d:\")]
    [InlineData("/usr/bin", "/usr/bin")]
    [InlineData("relative/path", "relative/path")]
    [InlineData(@"\\server\share", @"\\server\share")]
    [InlineData("", "")]
    public void NormalizeWindowsPath(string input, string expected)
    {
        Assert.Equal(expected, CwdUrl.NormalizeWindowsPath(input));
    }
}
