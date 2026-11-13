using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgHouse.Core.Ipc;

public static class AgEnv
{
    public const string Pipe = "AGHOUSE_PIPE";
    public const string PaneId = "AGHOUSE_PANE_ID";
    public const string WorkspaceId = "AGHOUSE_WORKSPACE_ID";

    /// <summary>Per-user pipe name. Overridable with AGHOUSE_PIPE (set in every pane).</summary>
    public static string PipeName =>
        Environment.GetEnvironmentVariable(Pipe) is { Length: > 0 } p ? p : DefaultPipeName;

    public static string DefaultPipeName
    {
        get
        {
            var user = new string(Environment.UserName.Where(char.IsLetterOrDigit).ToArray());
            return $"aghouse-{user.ToLowerInvariant()}";
        }
    }
}

public static class Methods
{
    public const string Ping = "ping";
    public const string Activate = "activate";
    public const string List = "list";
    public const string NewWorkspace = "new-workspace";
    public const string SelectWorkspace = "select-workspace";
    public const string RenameWorkspace = "rename-workspace";
    public const string NewSplit = "new-split";
    public const string FocusPane = "focus-pane";
    public const string ClosePane = "close-pane";
    public const string SendText = "send-text";
    public const string Notify = "notify";
    public const string SetStatus = "set-status";
    public const string ClearStatus = "clear-status";
    public const string SetProgress = "set-progress";
    public const string Log = "log";
    public const string NewTab = "new-tab";
    public const string MoveWorkspace = "move-workspace";
    public const string CloseWorkspace = "close-workspace";
    public const string ZoomPane = "zoom-pane";
    public const string Waiting = "waiting";
}

public sealed class RpcRequest
{
    public int Id { get; set; }
    public string Method { get; set; } = "";
    public Dictionary<string, string?> Params { get; set; } = [];

    public string? Get(string key) => Params.TryGetValue(key, out var v) ? v : null;
}

public sealed class RpcResponse
{
    public int Id { get; set; }
    public bool Ok { get; set; }
    public JsonNode? Result { get; set; }
    public string? Error { get; set; }

    public static RpcResponse Success(int id, JsonNode? result = null) => new() { Id = id, Ok = true, Result = result };
    public static RpcResponse Failure(int id, string error) => new() { Id = id, Ok = false, Error = error };
}

/// <summary>Newline-delimited JSON over a named pipe.</summary>
public static class RpcJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string line) => JsonSerializer.Deserialize<T>(line, Options);
}

public sealed class RpcClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private int _nextId;

    private RpcClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        var utf8 = new UTF8Encoding(false);
        _reader = new StreamReader(pipe, utf8, false, 4096, leaveOpen: true);
        _writer = new StreamWriter(pipe, utf8, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
    }

    public static async Task<RpcClient> ConnectAsync(string? pipeName = null, int timeoutMs = 2000, CancellationToken ct = default)
    {
        var pipe = new NamedPipeClientStream(".", pipeName ?? AgEnv.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeoutMs, ct).ConfigureAwait(false);
        return new RpcClient(pipe);
    }

    public async Task<RpcResponse> CallAsync(string method, Dictionary<string, string?>? parameters = null, CancellationToken ct = default)
    {
        var req = new RpcRequest { Id = Interlocked.Increment(ref _nextId), Method = method, Params = parameters ?? [] };
        await _writer.WriteLineAsync(RpcJson.Serialize(req).AsMemory(), ct).ConfigureAwait(false);
        var line = await _reader.ReadLineAsync(ct).ConfigureAwait(false)
                   ?? throw new IOException("aghouse closed the connection.");
        return RpcJson.Deserialize<RpcResponse>(line) ?? throw new IOException("Malformed response.");
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync().ConfigureAwait(false);
        _reader.Dispose();
        await _pipe.DisposeAsync().ConfigureAwait(false);
    }
}
