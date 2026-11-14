using System.Globalization;
using System.Text;

namespace AgHouse.Core.Terminal;

/// <summary>
/// Cleans a terminal title for reading in a sentence or a list. Agents put a status glyph in front
/// of their title: Claude Code shows "✳ Fix the build" at rest and a braille spinner ("⠋ Fix the
/// build") while it works. The glyph is a live status light, not part of the name, and UI fonts
/// often draw it as a box, so it is dropped.
/// </summary>
public static class TitleText
{
    /// <summary>The title without leading status glyphs and spaces. Returns the input if nothing would be left.</summary>
    public static string WithoutStatusGlyphs(string title)
    {
        var start = 0;
        foreach (var rune in title.EnumerateRunes())
        {
            if (!IsStatusGlyph(rune)) break;
            start += rune.Utf16SequenceLength;
        }
        var rest = title[start..].Trim();
        return rest.Length > 0 ? rest : title.Trim();
    }

    private static bool IsStatusGlyph(Rune rune)
    {
        if (Rune.IsWhiteSpace(rune)) return true;
        // Spinner frames that are punctuation rather than symbols: "·", "•", "*".
        if (rune.Value is '·' or '•' or '*') return true;
        // Dingbats (✳ ✶ ✻ ✽ ✢), braille spinners, arrows, emoji: all "other symbol".
        return Rune.GetUnicodeCategory(rune) == UnicodeCategory.OtherSymbol;
    }
}
