using System.Text;

namespace AgHouse.Core.Terminal;

/// <summary>
/// What a paste sends to a pane. Clipboard text is untrusted (a web page can put anything on it),
/// so control characters other than tab and line breaks are dropped: an ESC could otherwise end
/// bracketed paste early or drive the shell's line editor, and a NUL or ^C could act on the shell.
/// Line breaks become CR, which is what Enter sends.
/// </summary>
public static class PasteText
{
    public const string BracketStart = "\u001b[200~";
    public const string BracketEnd = "\u001b[201~";

    /// <summary>
    /// The text to send for a paste. With <paramref name="bracketed"/> (the program turned on
    /// bracketed paste) it is wrapped in ESC[200~ … ESC[201~, so the program takes it as typed text
    /// rather than as keys, and a line break doesn't run anything.
    /// </summary>
    public static string Prepare(string text, bool bracketed)
    {
        var sb = new StringBuilder(text.Length + (bracketed ? BracketStart.Length + BracketEnd.Length : 0));
        if (bracketed) sb.Append(BracketStart);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                sb.Append('\r');
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else if (c == '\n') sb.Append('\r');
            else if (c == '\t' || !IsControl(c)) sb.Append(c);
        }
        if (bracketed) sb.Append(BracketEnd);
        return sb.ToString();
    }

    /// <summary>
    /// True when pasting the text would press Enter at least once. A shell without bracketed paste
    /// runs each line as it arrives, including a line break hidden at the end of copied text.
    /// </summary>
    public static bool RunsLines(string text) => text.AsSpan().IndexOfAny('\r', '\n') >= 0;

    /// <summary>
    /// What plain Ctrl+V pastes. An image wins when the only text beside it is a single-line link,
    /// which a browser's "Copy image" can add; otherwise text wins, as in Windows Terminal (cells
    /// copied from Excel bring a picture of themselves too). Files copied in Explorer paste as paths.
    /// </summary>
    public static ClipboardPaste Choose(bool hasImage, bool hasFiles, string? text)
    {
        var hasText = !string.IsNullOrWhiteSpace(text);
        if (hasImage && (!hasText || IsLink(text!))) return ClipboardPaste.Image;
        if (hasText) return ClipboardPaste.Text;
        if (hasFiles) return ClipboardPaste.Files;
        return ClipboardPaste.None;
    }

    /// <summary>
    /// Copied files as typed text, the way Windows Terminal pastes them: one path after another,
    /// quoted when a path has a space. Claude Code attaches an image whose path is pasted.
    /// </summary>
    public static string FilePaths(IEnumerable<string?> paths) =>
        string.Join(" ", paths.Where(p => !string.IsNullOrEmpty(p)).Select(p => p!.Contains(' ') ? $"\"{p}\"" : p));

    private static bool IsLink(string text)
    {
        var t = text.Trim();
        return !RunsLines(t)
            && (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("file:", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>C0 (including ESC and NUL), DEL and C1 controls.</summary>
    private static bool IsControl(char c) => c < ' ' || c is >= '\u007f' and <= '\u009f';
}

/// <summary>What plain Ctrl+V does with the clipboard (<see cref="PasteText.Choose"/>).</summary>
public enum ClipboardPaste
{
    /// <summary>Nothing to paste: the key goes to the terminal.</summary>
    None,
    /// <summary>Send Alt+V, Claude Code's image-paste key on Windows; Claude reads the image itself.</summary>
    Image,
    /// <summary>Paste the copied files' paths.</summary>
    Files,
    /// <summary>Paste the text, as Ctrl+Shift+V does.</summary>
    Text,
}
