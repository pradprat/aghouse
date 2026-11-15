using AgHouse.Core.Terminal;

namespace AgHouse.Tests;

/// <summary>What plain Ctrl+V pastes (<see cref="PasteText.Choose"/>).</summary>
public class CtrlVPasteTests
{
    [Theory]
    // A screenshot: only a picture.
    [InlineData(true, false, null, ClipboardPaste.Image)]
    [InlineData(true, false, "  ", ClipboardPaste.Image)]
    // A browser's "Copy image" can add the image's address as text.
    [InlineData(true, false, "https://example.com/cat.png", ClipboardPaste.Image)]
    [InlineData(true, false, "data:image/png;base64,AAAA", ClipboardPaste.Image)]
    // Excel cells (or a Word paragraph) bring a picture of themselves: the text is what's meant.
    [InlineData(true, false, "a\tb\r\nc\td\r\n", ClipboardPaste.Text)]
    [InlineData(true, false, "hello", ClipboardPaste.Text)]
    [InlineData(true, false, "https://a.example\nhttps://b.example", ClipboardPaste.Text)]
    // Plain text, and the text an app may put beside copied files.
    [InlineData(false, false, "git status", ClipboardPaste.Text)]
    [InlineData(false, true, "C:\\notes.txt", ClipboardPaste.Text)]
    // Files copied in Explorer.
    [InlineData(false, true, null, ClipboardPaste.Files)]
    // Nothing: Ctrl+V goes to the terminal.
    [InlineData(false, false, null, ClipboardPaste.None)]
    [InlineData(false, false, "", ClipboardPaste.None)]
    public void Choose(bool image, bool files, string? text, ClipboardPaste expected) =>
        Assert.Equal(expected, PasteText.Choose(image, files, text));

    [Fact]
    public void FilePaths_QuotesOnlyPathsWithSpaces()
    {
        Assert.Equal(@"C:\shot.png ""C:\My Pictures\cat 1.png""",
            PasteText.FilePaths([@"C:\shot.png", null, "", @"C:\My Pictures\cat 1.png"]));
    }
}
