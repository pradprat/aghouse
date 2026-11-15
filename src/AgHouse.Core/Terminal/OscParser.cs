using System.Text;
using System.Text.RegularExpressions;

namespace AgHouse.Core.Terminal;

/// <summary>Something interesting the shell/app emitted in its output stream.</summary>
public abstract record TerminalEvent;

/// <summary>Desktop notification request (OSC 9 / OSC 99 / OSC 777).</summary>
public sealed record NotifyEvent(string? Title, string Body) : TerminalEvent;

/// <summary>Working directory report (OSC 7 file URL, or ConEmu OSC 9;9).</summary>
public sealed record CwdEvent(string Path) : TerminalEvent;

/// <summary>Window/tab title change (OSC 0 / OSC 2).</summary>
public sealed record TitleEvent(string Title) : TerminalEvent;

/// <summary>A bare BEL outside of any escape sequence.</summary>
public sealed record BellEvent : TerminalEvent
{
    public static readonly BellEvent Instance = new();
}

/// <summary>
/// Streaming scanner for the handful of escape sequences aghouse cares about.
/// It never modifies the stream; it only observes it. Sequences may be split
/// across arbitrary chunk boundaries, so all state is kept between <see cref="Feed"/> calls.
/// Not thread-safe: feed it from a single reader thread.
/// </summary>
public sealed partial class OscParser
{
    private const char Esc = '\x1b';
    private const char Bel = '\a';
    private const char C1Osc = '\x9d';
    private const char C1St = '\x9c';
    private const int MaxOscLength = 16 * 1024;

    private const char C1Csi = '\x9b';

    private enum State { Ground, Escape, Osc, OscEscape, SkipString, SkipStringEscape, Csi }

    private State _state = State.Ground;
    private readonly StringBuilder _osc = new();
    private bool _oscOverflow;

    // OSC 99 (kitty) notifications can be chunked; keyed by the 'i' id.
    private readonly Dictionary<string, (string? Title, string? Body)> _kittyPending = new();

    /// <summary>Unfinished OSC 99 notifications kept at once. Each is also capped at <see cref="MaxOscLength"/>.</summary>
    public const int MaxKittyPending = 8;

    // CSI parameters and intermediates, only kept to spot DECSET/DECRST 2004 (bracketed paste).
    private readonly StringBuilder _csi = new();
    private const int MaxCsiLength = 32;

    private volatile bool _bracketedPaste;

    /// <summary>
    /// Whether the program in the pane asked for bracketed paste (CSI ? 2004 h) and hasn't turned it
    /// off (CSI ? 2004 l). Read from the UI thread while the reader thread feeds the parser.
    /// </summary>
    public bool BracketedPaste => _bracketedPaste;

    public void Feed(ReadOnlySpan<char> data, List<TerminalEvent> events)
    {
        foreach (var c in data)
        {
            Step(c, events);
        }
    }

    private void Step(char c, List<TerminalEvent> events)
    {
        switch (_state)
        {
            case State.Ground:
                if (c == Esc) _state = State.Escape;
                else if (c == Bel) events.Add(BellEvent.Instance);
                else if (c == C1Osc) BeginOsc();
                else if (c == C1Csi) BeginCsi();
                break;

            case State.Escape:
                switch (c)
                {
                    case ']': BeginOsc(); break;
                    case '[': BeginCsi(); break;
                    // DCS, SOS, PM, APC: opaque strings terminated by ST. A BEL inside them is not a bell.
                    case 'P' or 'X' or '^' or '_': _state = State.SkipString; break;
                    case Esc: break; // ESC ESC: stay
                    default: _state = State.Ground; break;
                }
                break;

            case State.Osc:
                if (c == Bel || c == C1St) { Dispatch(events); _state = State.Ground; }
                else if (c == Esc) _state = State.OscEscape;
                else Append(c);
                break;

            case State.OscEscape:
                if (c == '\\') { Dispatch(events); _state = State.Ground; }
                else
                {
                    // Aborted OSC; the ESC starts a new sequence.
                    _osc.Clear();
                    _state = State.Escape;
                    Step(c, events);
                }
                break;

            case State.SkipString:
                if (c == Esc) _state = State.SkipStringEscape;
                else if (c == C1St) _state = State.Ground;
                break;

            case State.SkipStringEscape:
                _state = c == '\\' ? State.Ground : c == Esc ? State.SkipStringEscape : State.SkipString;
                break;

            case State.Csi:
                if (c is >= '\x40' and <= '\x7e') { DispatchCsi(c); _state = State.Ground; }
                else if (c is >= '\x20' and <= '\x3f') { if (_csi.Length < MaxCsiLength) _csi.Append(c); }
                else if (c == Esc) _state = State.Escape; // aborted: the ESC starts a new sequence
                else if (c == Bel) { events.Add(BellEvent.Instance); _state = State.Ground; } // not valid inside CSI
                else if (c >= ' ') _state = State.Ground; // anything else ends a malformed CSI
                break;
        }
    }

    private void BeginCsi()
    {
        _csi.Clear();
        _state = State.Csi;
    }

    private void DispatchCsi(char final)
    {
        if (final is not ('h' or 'l') || _csi.Length == 0 || _csi[0] != '?') return;
        // CSI ? 1 ; 2004 h sets several private modes at once.
        foreach (var mode in _csi.ToString(1, _csi.Length - 1).Split(';'))
        {
            if (mode == "2004") _bracketedPaste = final == 'h';
        }
    }

    private void BeginOsc()
    {
        _osc.Clear();
        _oscOverflow = false;
        _state = State.Osc;
    }

    private void Append(char c)
    {
        if (_osc.Length < MaxOscLength) _osc.Append(c);
        else _oscOverflow = true;
    }

    private void Dispatch(List<TerminalEvent> events)
    {
        if (_oscOverflow) { _osc.Clear(); return; }
        var s = _osc.ToString();
        _osc.Clear();

        var semi = s.IndexOf(';');
        var code = semi < 0 ? s : s[..semi];
        var rest = semi < 0 ? "" : s[(semi + 1)..];

        switch (code)
        {
            case "0" or "2":
                events.Add(new TitleEvent(rest));
                break;

            case "7":
                if (CwdUrl.TryParse(rest, out var path)) events.Add(new CwdEvent(path));
                break;

            case "9":
                HandleOsc9(rest, events);
                break;

            case "777":
                HandleOsc777(rest, events);
                break;

            case "99":
                HandleOsc99(rest, events);
                break;
        }
    }

    [GeneratedRegex(@"^\d+(;|$)")]
    private static partial Regex ConEmuSubcommand();

    private static void HandleOsc9(string rest, List<TerminalEvent> events)
    {
        if (rest.StartsWith("9;", StringComparison.Ordinal))
        {
            var p = rest[2..].Trim().Trim('"');
            if (p.Length > 0) events.Add(new CwdEvent(CwdUrl.NormalizeWindowsPath(p)));
            return;
        }
        // Other ConEmu sub-commands (9;4 progress, 9;1 sleep, ...) are not notifications.
        if (ConEmuSubcommand().IsMatch(rest)) return;
        if (rest.Length > 0) events.Add(new NotifyEvent(null, rest));
    }

    private static void HandleOsc777(string rest, List<TerminalEvent> events)
    {
        // 777;notify;<title>;<body>
        var parts = rest.Split(';', 3);
        if (parts.Length >= 2 && parts[0] == "notify")
        {
            var title = parts[1];
            var body = parts.Length == 3 ? parts[2] : "";
            events.Add(new NotifyEvent(title.Length > 0 ? title : null, body));
        }
    }

    private void HandleOsc99(string rest, List<TerminalEvent> events)
    {
        // 99;<k=v:k=v>;<payload>
        var semi = rest.IndexOf(';');
        if (semi < 0) return;
        var meta = rest[..semi];
        var payload = rest[(semi + 1)..];

        string id = "", part = "title";
        bool done = true, base64 = false;
        foreach (var kv in meta.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = kv.IndexOf('=');
            if (eq <= 0) continue;
            var k = kv[..eq];
            var v = kv[(eq + 1)..];
            switch (k)
            {
                case "i": id = v; break;
                case "d": done = v != "0"; break;
                case "p": part = v; break;
                case "e": base64 = v == "1"; break;
            }
        }

        if (base64)
        {
            try { payload = Encoding.UTF8.GetString(Convert.FromBase64String(payload)); }
            catch (FormatException) { return; }
        }

        if (part is not ("title" or "body")) return; // queries / unknown parts

        _kittyPending.TryGetValue(id, out var acc);
        if (part == "title") acc.Title = (acc.Title ?? "") + payload;
        else acc.Body = (acc.Body ?? "") + payload;

        // Untrusted output can send unfinished chunks forever, under one id or many: cap both.
        if ((acc.Title?.Length ?? 0) + (acc.Body?.Length ?? 0) > MaxOscLength)
        {
            _kittyPending.Remove(id);
            return;
        }

        if (!done)
        {
            if (!_kittyPending.ContainsKey(id) && _kittyPending.Count >= MaxKittyPending) return;
            _kittyPending[id] = acc;
            return;
        }

        _kittyPending.Remove(id);
        if (acc.Body is null) events.Add(new NotifyEvent(null, acc.Title ?? ""));
        else events.Add(new NotifyEvent(acc.Title, acc.Body));
    }
}
