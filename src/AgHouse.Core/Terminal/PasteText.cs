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

    /// <summary>C0 (including ESC and NUL), DEL and C1 controls.</summary>
    private static bool IsControl(char c) => c < ' ' || c is >= '\u007f' and <= '\u009f';
}
