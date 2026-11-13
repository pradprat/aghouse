namespace AgHouse.App.Surfaces;

/// <summary>One row in the command palette: a waiting pane, a workspace, a pane or an action.</summary>
public sealed class PaletteItem
{
    public required string Group { get; init; }
    public required string Title { get; init; }
    public string? Detail { get; init; }
    /// <summary>Its shortcut, shown at the right like a menu's input gesture text.</summary>
    public string? Gesture { get; init; }
    /// <summary>A waiting pane: the row starts with "Needs you".</summary>
    public bool NeedsYou { get; init; }
    /// <summary>Extra words to match on ("tab", "split", "settings.json").</summary>
    public string? Keywords { get; init; }
    public required Action Run { get; init; }
    /// <summary>Order within the palette when nothing is typed.</summary>
    public int Order { get; init; }
    /// <summary>A number typed into the palette selects this workspace ("3").</summary>
    public int? WorkspaceNumber { get; init; }

    public double Score { get; set; }

    public string Prefix => NeedsYou ? "Needs you · " : "";
    public string DetailText => string.IsNullOrEmpty(Detail) ? "" : "   " + Detail;

    /// <summary>
    /// How well <paramref name="query"/> matches: every word must appear somewhere. Matches at the
    /// start of the title rank highest, then at the start of a word, then anywhere. -1 = no match.
    /// </summary>
    public double Match(string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return 0;
        if (words.Length == 1 && int.TryParse(words[0], out var n) && n == WorkspaceNumber) return 100;
        double score = 0;
        foreach (var word in words)
        {
            var at = Title.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (at == 0) score += 4;
            else if (at > 0 && !char.IsLetterOrDigit(Title[at - 1])) score += 3;
            else if (at > 0) score += 1.5;
            else if ((Detail ?? "").Contains(word, StringComparison.OrdinalIgnoreCase)
                     || (Keywords ?? "").Contains(word, StringComparison.OrdinalIgnoreCase)) score += 1;
            else return -1;
        }
        return score + (NeedsYou ? 0.5 : 0); // a waiting pane wins a tie
    }
}
