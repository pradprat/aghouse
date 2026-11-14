using AgHouse.Core.Terminal;

namespace AgHouse.Tests;

public class KeyChordsTests
{
    [Theory]
    [InlineData("ctrl+c", "\x03")]
    [InlineData("Ctrl+C", "\x03")]
    [InlineData("control+d", "\x04")]
    [InlineData("ctrl+z", "\x1a")]
    [InlineData("ctrl+[", "\x1b")]
    [InlineData("ctrl+space", "\0")]
    [InlineData("enter", "\r")]
    [InlineData("return", "\r")]
    [InlineData("tab", "\t")]
    [InlineData("shift+tab", "\x1b[Z")]
    [InlineData("esc", "\x1b")]
    [InlineData("backspace", "\x7f")]
    [InlineData("ctrl+backspace", "\b")]
    [InlineData("space", " ")]
    [InlineData("up", "\x1b[A")]
    [InlineData("down", "\x1b[B")]
    [InlineData("right", "\x1b[C")]
    [InlineData("left", "\x1b[D")]
    [InlineData("home", "\x1b[H")]
    [InlineData("end", "\x1b[F")]
    [InlineData("ctrl+left", "\x1b[1;5D")]
    [InlineData("shift+up", "\x1b[1;2A")]
    [InlineData("alt+right", "\x1b[1;3C")]
    [InlineData("delete", "\x1b[3~")]
    [InlineData("pgup", "\x1b[5~")]
    [InlineData("pagedown", "\x1b[6~")]
    [InlineData("ctrl+delete", "\x1b[3;5~")]
    [InlineData("f1", "\x1bOP")]
    [InlineData("f4", "\x1bOS")]
    [InlineData("shift+f1", "\x1b[1;2P")]
    [InlineData("f5", "\x1b[15~")]
    [InlineData("f12", "\x1b[24~")]
    [InlineData("alt+x", "\x1bx")]
    [InlineData("alt+enter", "\x1b\r")]
    [InlineData("ctrl+alt+c", "\x1b\x03")]
    [InlineData("shift+a", "A")]
    [InlineData("y", "y")]
    [InlineData("+", "+")]
    [InlineData("Q", "Q")]
    public void Chord_ToSequence(string chord, string expected)
    {
        Assert.Equal(expected, KeyChords.ToSequence(chord));
    }

    [Fact]
    public void SeveralChords_AreConcatenated()
    {
        Assert.Equal("\x1b[A\r", KeyChords.ToSequence(["up", "enter"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hyper+c")]
    [InlineData("ctrl+1")]
    [InlineData("f13")]
    [InlineData("nope")]
    public void UnknownKey_Throws_WithTheNames(string chord)
    {
        var ex = Assert.Throws<ArgumentException>(() => KeyChords.ToSequence(chord));
        Assert.Contains("enter", ex.Message);
    }

    [Fact]
    public void EveryListedName_Works()
    {
        foreach (var name in KeyChords.Names) Assert.NotEmpty(KeyChords.ToSequence(name));
    }
}
