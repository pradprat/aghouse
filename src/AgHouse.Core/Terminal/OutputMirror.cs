using System.Text;

namespace AgHouse.Core.Terminal;

/// <summary>
/// A plain-text copy of what a pane printed, for find. The native terminal control keeps its own
/// buffer but exposes no search, so aghouse keeps a mirror from the same output stream.
///
/// This is not a terminal emulator. It strips escape sequences and keeps lines in the order they
/// arrived. ConPTY compresses runs of spaces into "cursor forward", so CSI n C becomes n spaces.
/// A cursor jump ends the current line. A bare CR starts the line over, the way a progress bar
/// redraws itself. A screen that ConPTY repaints can therefore appear twice; find collapses
/// identical lines.
///
/// Thread-safe: the reader thread feeds it while the UI thread searches.
/// </summary>
public sealed class OutputMirror(int maxLines = 10_000)
{
    private const int MaxLineLength = 4_000;

    private enum State { Ground, Escape, Csi, String, StringEscape }

    private readonly object _gate = new();
    private readonly Queue<string> _lines = new();
    private readonly StringBuilder _line = new();
    private readonly StringBuilder _params = new();
    private State _state;
    private bool _pendingCr;
    private long _dropped; // lines that fell off the front: a line's cursor is _dropped + its index

    public int MaxLines { get; } = Math.Max(1, maxLines);

    public void Feed(ReadOnlySpan<char> data)
    {
        lock (_gate)
        {
            foreach (var c in data) Step(c);
        }
    }

    private void Step(char c)
    {
        switch (_state)
        {
            case State.Ground:
                Ground(c);
                break;

            case State.Escape:
                _state = c switch
                {
                    '[' => State.Csi,
                    ']' or 'P' or 'X' or '^' or '_' => State.String, // OSC, DCS, SOS, PM, APC
                    _ => State.Ground, // a two-character sequence (ESC 7, ESC =, ...)
                };
                _params.Clear();
                break;

            case State.Csi:
                if (c is >= '\x40' and <= '\x7e')
                {
                    Csi(c);
                    _state = State.Ground;
                }
                else if (_params.Length < 32) _params.Append(c);
                break;

            case State.String:
                if (c == '\a') _state = State.Ground;
                else if (c == '\x1b') _state = State.StringEscape;
                break;

            case State.StringEscape:
                _state = c == '\\' ? State.Ground : State.String;
                break;
        }
    }

    private void Ground(char c)
    {
        if (_pendingCr)
        {
            _pendingCr = false;
            if (c != '\n') _line.Clear(); // CR without LF: the line is being redrawn
        }

        switch (c)
        {
            case '\x1b': _state = State.Escape; break;
            case '\r': _pendingCr = true; break;
            case '\n': Commit(); break;
            case '\b': if (_line.Length > 0) _line.Length--; break;
            case '\t': Append("    "); break;
            case < ' ' or '\x7f': break;
            default: if (_line.Length < MaxLineLength) _line.Append(c); break;
        }
    }

    private void Csi(char final)
    {
        switch (final)
        {
            case 'C': // cursor forward: ConPTY's way of printing a run of spaces
                Append(new string(' ', Math.Clamp(FirstParam(1), 1, 512)));
                break;
            case 'H' or 'f' or 'd' or 'A' or 'B' or 'E' or 'F':
                // The cursor moved to another line; what was on this one is done.
                if (_line.ToString().Trim().Length > 0) Commit();
                else _line.Clear();
                break;
            case 'J' when FirstParam(0) >= 2:
                if (_line.ToString().Trim().Length > 0) Commit();
                else _line.Clear();
                break;
        }
    }

    private int FirstParam(int fallback)
    {
        var s = _params.ToString();
        var semi = s.IndexOf(';');
        if (semi >= 0) s = s[..semi];
        return int.TryParse(s, out var n) && n > 0 ? n : fallback;
    }

    private void Append(string s)
    {
        if (_line.Length + s.Length <= MaxLineLength) _line.Append(s);
    }

    private void Commit()
    {
        _lines.Enqueue(_line.ToString().TrimEnd());
        _line.Clear();
        while (_lines.Count > MaxLines)
        {
            _lines.Dequeue();
            _dropped++;
        }
    }

    /// <summary>
    /// What a script reads back (<c>aghouse read-pane</c>). With <paramref name="since"/>, the finished
    /// lines from that cursor on; without it, the last <paramref name="last"/> finished lines. The line
    /// still being written (a prompt, "Password:") comes separately in <see cref="MirrorRead.Partial"/>,
    /// because it can still change. Pass <see cref="MirrorRead.Next"/> back as <paramref name="since"/>
    /// to get only what came after.
    /// </summary>
    public MirrorRead Read(long? since = null, int last = 50)
    {
        lock (_gate)
        {
            var total = _dropped + _lines.Count;
            long start;
            var truncated = false;
            if (since is { } from)
            {
                start = Math.Clamp(from, 0, total);
                if (start < _dropped)
                {
                    truncated = true; // some of what was asked for has been dropped
                    start = _dropped;
                }
            }
            else
            {
                start = Math.Max(_dropped, total - Math.Max(0, last));
            }
            var lines = _lines.Skip((int)(start - _dropped)).ToList();
            var partial = _line.ToString().TrimEnd();
            return new MirrorRead(lines, partial.Length > 0 ? partial : null, total, truncated);
        }
    }

    /// <summary>Every line so far, oldest first, including the line still being written.</summary>
    public IReadOnlyList<string> Lines()
    {
        lock (_gate)
        {
            var list = new List<string>(_lines.Count + 1);
            list.AddRange(_lines);
            if (_line.Length > 0) list.Add(_line.ToString().TrimEnd());
            return list;
        }
    }

    /// <summary>
    /// Lines containing <paramref name="query"/> (ignoring case), newest first, identical lines
    /// folded into one with a count. At most <paramref name="max"/> results.
    /// </summary>
    public IReadOnlyList<FindMatch> Find(string query, int max = 200)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var lines = Lines();
        var results = new List<FindMatch>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var line = lines[i];
            var at = line.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;
            if (seen.TryGetValue(line, out var existing))
            {
                results[existing] = results[existing] with { Count = results[existing].Count + 1 };
                continue;
            }
            if (results.Count >= max) continue; // keep counting repeats of the ones already listed
            seen[line] = results.Count;
            results.Add(new FindMatch(line.Trim(), 1));
        }
        return results;
    }
}

/// <summary>
/// Finished lines, the line still being written (null if none), the cursor to read from next time,
/// and whether lines that were asked for had already been dropped (the mirror keeps the last MaxLines).
/// </summary>
public sealed record MirrorRead(IReadOnlyList<string> Lines, string? Partial, long Next, bool Truncated);

/// <summary>One line that matched, and how many times that exact line appeared.</summary>
public sealed record FindMatch(string Line, int Count);
