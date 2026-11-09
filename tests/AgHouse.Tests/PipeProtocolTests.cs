using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgHouse.App.Ipc;
using AgHouse.Core.Ipc;

namespace AgHouse.Tests;

/// <summary>Pure (no pipe) tests of the JSON wire format in <c>Protocol.cs</c>.</summary>
public class RpcWireFormatTests
{
    [Fact]
    public void Request_UsesCamelCaseKeys()
    {
        var req = new RpcRequest { Id = 1, Method = Methods.Ping, Params = { ["pane"] = "p1" } };
        Assert.Equal("""{"id":1,"method":"ping","params":{"pane":"p1"}}""", RpcJson.Serialize(req));
    }

    [Fact]
    public void Response_UsesCamelCaseKeys()
    {
        Assert.Equal("""{"id":2,"ok":false,"result":null,"error":"nope"}""", RpcJson.Serialize(RpcResponse.Failure(2, "nope")));
    }

    [Fact]
    public void Request_MissingFields_GetDefaults()
    {
        var req = RpcJson.Deserialize<RpcRequest>("{}")!;
        Assert.Equal(0, req.Id);
        Assert.Equal("", req.Method);
        Assert.Empty(req.Params);
        Assert.Null(req.Get("anything"));
    }

    [Fact]
    public void Request_PropertyNamesAreCaseInsensitive()
    {
        var req = RpcJson.Deserialize<RpcRequest>("""{"ID":9,"METHOD":"list","Params":{"a":"b"}}""")!;
        Assert.Equal(9, req.Id);
        Assert.Equal("list", req.Method);
        Assert.Equal("b", req.Get("a"));
    }

    [Fact]
    public void Request_UnknownFieldsAreIgnored()
    {
        var req = RpcJson.Deserialize<RpcRequest>("""{"id":1,"method":"ping","future":{"x":1}}""")!;
        Assert.Equal("ping", req.Method);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("""{"id":"abc"}""")]
    [InlineData("""{"id":1,"params":{"n":5}}""")]
    [InlineData("""{"id":1,"params":["a"]}""")]
    public void Request_MalformedInput_ThrowsJsonException(string line)
    {
        Assert.Throws<JsonException>(() => RpcJson.Deserialize<RpcRequest>(line));
    }

    [Fact]
    public void Request_NullLiteral_DeserializesToNull()
    {
        Assert.Null(RpcJson.Deserialize<RpcRequest>("null"));
    }

    [Fact]
    public void Failure_RoundTrip_KeepsIdAndError()
    {
        var back = RpcJson.Deserialize<RpcResponse>(RpcJson.Serialize(RpcResponse.Failure(5, "bad pane")))!;
        Assert.False(back.Ok);
        Assert.Equal(5, back.Id);
        Assert.Equal("bad pane", back.Error);
        Assert.Null(back.Result);
    }

    [Fact]
    public void Success_WithoutResult_RoundTrips()
    {
        var back = RpcJson.Deserialize<RpcResponse>(RpcJson.Serialize(RpcResponse.Success(4)))!;
        Assert.True(back.Ok);
        Assert.Null(back.Result);
        Assert.Null(back.Error);
    }

    [Fact]
    public void ControlCharactersAndUnicode_StayOnOneLine_AndRoundTrip()
    {
        // send-text --enter appends "\r"; the wire format is one JSON object per line, so line breaks must be escaped.
        const string text = "echo héllo ✓ 日本語\r\nsecond \"quoted\" \\ line\t\u0001";
        var line = RpcJson.Serialize(new RpcRequest { Id = 1, Method = Methods.SendText, Params = { ["text"] = text } });
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.Equal(text, RpcJson.Deserialize<RpcRequest>(line)!.Get("text"));
    }

    [Fact]
    public void NestedResult_RoundTrips()
    {
        var result = new JsonObject
        {
            ["workspaces"] = new JsonArray(new JsonObject { ["id"] = "w1", ["panes"] = new JsonArray("p1", "p2") }),
        };
        var back = RpcJson.Deserialize<RpcResponse>(RpcJson.Serialize(RpcResponse.Success(1, result)))!;
        Assert.Equal("p2", back.Result!["workspaces"]![0]!["panes"]![1]!.GetValue<string>());
    }

    [Fact]
    public void Methods_AreUniqueKebabCase_AndCoverTheCliCommands()
    {
        var all = typeof(Methods).GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(all, m => Assert.Matches("^[a-z]+(-[a-z]+)*$", m));
        foreach (var expected in new[] { "ping", "activate", "list", "new-workspace", "select-workspace", "rename-workspace",
                     "new-split", "focus-pane", "close-pane", "send-text", "notify" })
        {
            Assert.Contains(expected, all);
        }
    }

    [Fact]
    public void AgEnv_Names_AreStable()
    {
        // Every pane gets these; scripts and the Claude hook depend on the exact names.
        Assert.Equal("AGHOUSE_PIPE", AgEnv.Pipe);
        Assert.Equal("AGHOUSE_PANE_ID", AgEnv.PaneId);
        Assert.Equal("AGHOUSE_WORKSPACE_ID", AgEnv.WorkspaceId);
    }
}

/// <summary>AgEnv reads a process-wide environment variable, so these tests must not run alongside each other.</summary>
[Collection("AGHOUSE_PIPE environment variable")]
public class AgEnvPipeNameTests
{
    [Fact]
    public void PipeName_UsesOverrideFromEnvironment()
    {
        var old = Environment.GetEnvironmentVariable(AgEnv.Pipe);
        try
        {
            Environment.SetEnvironmentVariable(AgEnv.Pipe, "custom-pipe");
            Assert.Equal("custom-pipe", AgEnv.PipeName);
        }
        finally { Environment.SetEnvironmentVariable(AgEnv.Pipe, old); }
    }

    [Fact]
    public void PipeName_EmptyOverrideFallsBackToDefault()
    {
        var old = Environment.GetEnvironmentVariable(AgEnv.Pipe);
        try
        {
            Environment.SetEnvironmentVariable(AgEnv.Pipe, "");
            Assert.Equal(AgEnv.DefaultPipeName, AgEnv.PipeName);
        }
        finally { Environment.SetEnvironmentVariable(AgEnv.Pipe, old); }
    }
}

/// <summary>
/// Runs the real <see cref="PipeServer"/> (compiled in from the App project, no GUI involved)
/// against the real <see cref="RpcClient"/>, over a uniquely named pipe.
/// </summary>
public sealed class PipeRoundTripTests : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    private readonly string _pipeName = "aghouse-test-" + Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource _cts = new(Limit);
    private readonly PipeServer _server;
    private readonly List<RpcRequest> _seen = [];

    public PipeRoundTripTests()
    {
        _server = new PipeServer(_pipeName, HandleAsync);
        _server.Start();
    }

    public void Dispose()
    {
        _server.Dispose();
        _cts.Dispose();
    }

    private async Task<RpcResponse> HandleAsync(RpcRequest request)
    {
        lock (_seen) _seen.Add(request);
        await Task.Yield();
        if (request.Method == "boom") throw new InvalidOperationException("kaboom");

        var echoed = new JsonObject();
        foreach (var (k, v) in request.Params) echoed[k] = v;
        return RpcResponse.Success(request.Id, new JsonObject { ["method"] = request.Method, ["params"] = echoed });
    }

    private Task<RpcClient> ConnectAsync() => RpcClient.ConnectAsync(_pipeName, 5000, _cts.Token);

    [Fact]
    public async Task Call_RoundTrips_MethodAndParams()
    {
        await using var client = await ConnectAsync();
        var res = await client.CallAsync(Methods.NewSplit, new() { ["direction"] = "down", ["pane"] = null }, _cts.Token);

        Assert.True(res.Ok);
        Assert.Equal("new-split", res.Result!["method"]!.GetValue<string>());
        Assert.Equal("down", res.Result["params"]!["direction"]!.GetValue<string>());
        Assert.Null(res.Result["params"]!["pane"]);
    }

    [Fact]
    public async Task Call_WithoutParams_Works()
    {
        await using var client = await ConnectAsync();
        var res = await client.CallAsync(Methods.Ping, ct: _cts.Token);
        Assert.True(res.Ok);
        Assert.Empty(_seen.Single().Params);
    }

    [Fact]
    public async Task SequentialCalls_OnOneConnection_GetMatchingIncreasingIds()
    {
        await using var client = await ConnectAsync();
        for (var expected = 1; expected <= 5; expected++)
        {
            var res = await client.CallAsync(Methods.List, ct: _cts.Token);
            Assert.Equal(expected, res.Id);
        }
        Assert.Equal([1, 2, 3, 4, 5], _seen.Select(r => r.Id));
    }

    [Fact]
    public async Task SendText_WithNewlinesAndUnicode_ArrivesIntact()
    {
        const string text = "line1\r\nline2 ✓ \"q\"\r";
        await using var client = await ConnectAsync();
        var res = await client.CallAsync(Methods.SendText, new() { ["pane"] = "p1", ["text"] = text }, _cts.Token);

        Assert.Equal(text, res.Result!["params"]!["text"]!.GetValue<string>());
        Assert.Equal(text, _seen.Single().Get("text"));
    }

    [Fact]
    public async Task HandlerException_BecomesFailure_AndConnectionStaysUsable()
    {
        await using var client = await ConnectAsync();

        var failed = await client.CallAsync("boom", ct: _cts.Token);
        Assert.False(failed.Ok);
        Assert.Equal("kaboom", failed.Error);
        Assert.Equal(1, failed.Id);

        var next = await client.CallAsync(Methods.Ping, ct: _cts.Token);
        Assert.True(next.Ok);
        Assert.Equal(2, next.Id);
    }

    [Fact]
    public async Task ConcurrentClients_EachGetTheirOwnAnswers()
    {
        var tasks = Enumerable.Range(0, 6).Select(async i =>
        {
            await using var client = await ConnectAsync();
            var res = await client.CallAsync(Methods.Notify, new() { ["body"] = $"client-{i}" }, _cts.Token);
            return res.Result!["params"]!["body"]!.GetValue<string>();
        });

        var bodies = await Task.WhenAll(tasks);
        Assert.Equal(Enumerable.Range(0, 6).Select(i => $"client-{i}"), bodies.OrderBy(b => b, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ClientDisconnect_DoesNotStopServer()
    {
        await using (var first = await ConnectAsync())
        {
            await first.CallAsync(Methods.Ping, ct: _cts.Token);
        }

        await using var second = await ConnectAsync();
        Assert.True((await second.CallAsync(Methods.Ping, ct: _cts.Token)).Ok);
    }

    [Fact]
    public async Task Connect_ToMissingPipe_TimesOut()
    {
        // The CLI maps TimeoutException to "aghouse is not running."
        await Assert.ThrowsAsync<TimeoutException>(() =>
            RpcClient.ConnectAsync("aghouse-test-missing-" + Guid.NewGuid().ToString("N"), 200, _cts.Token));
    }

    // ---- Malformed input, sent as raw lines so the client's own serializer can't sanitize it ----

    private sealed class RawLink : IAsyncDisposable
    {
        private readonly NamedPipeClientStream _pipe;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly CancellationToken _ct;

        private RawLink(NamedPipeClientStream pipe, CancellationToken ct)
        {
            _pipe = pipe;
            _ct = ct;
            var utf8 = new UTF8Encoding(false);
            _reader = new StreamReader(pipe, utf8, false, 4096, leaveOpen: true);
            _writer = new StreamWriter(pipe, utf8, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        }

        public static async Task<RawLink> OpenAsync(string name, CancellationToken ct)
        {
            var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(5000, ct);
            return new RawLink(pipe, ct);
        }

        public Task SendAsync(string line) => _writer.WriteLineAsync(line.AsMemory(), _ct);

        public async Task<RpcResponse> ReadResponseAsync()
        {
            var line = await _reader.ReadLineAsync(_ct) ?? throw new IOException("server closed the pipe");
            return RpcJson.Deserialize<RpcResponse>(line)!;
        }

        public async ValueTask DisposeAsync()
        {
            await _writer.DisposeAsync();
            _reader.Dispose();
            await _pipe.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("""{"id":"abc","method":"ping"}""")]
    [InlineData("""{"id":1,"method":"ping","params":{"n":5}}""")] // params must be strings; numbers are rejected
    public async Task MalformedRequest_GetsFailureWithId0_AndConnectionSurvives(string badLine)
    {
        await using var link = await RawLink.OpenAsync(_pipeName, _cts.Token);

        await link.SendAsync(badLine);
        var bad = await link.ReadResponseAsync();
        Assert.False(bad.Ok);
        Assert.Equal(0, bad.Id);
        Assert.StartsWith("Malformed request:", bad.Error);

        await link.SendAsync(RpcJson.Serialize(new RpcRequest { Id = 42, Method = Methods.Ping }));
        var good = await link.ReadResponseAsync();
        Assert.True(good.Ok);
        Assert.Equal(42, good.Id);
        Assert.Equal(42, Assert.Single(_seen).Id); // the bad line never reached the handler
    }

    [Fact]
    public async Task NullRequest_GetsEmptyRequestFailure()
    {
        await using var link = await RawLink.OpenAsync(_pipeName, _cts.Token);
        await link.SendAsync("null");
        var res = await link.ReadResponseAsync();
        Assert.False(res.Ok);
        Assert.Equal("Empty request.", res.Error);
        Assert.Empty(_seen);
    }

    [Fact]
    public async Task BlankLines_AreSkipped_WithoutAResponse()
    {
        await using var link = await RawLink.OpenAsync(_pipeName, _cts.Token);
        await link.SendAsync("");
        await link.SendAsync("");
        await link.SendAsync(RpcJson.Serialize(new RpcRequest { Id = 7, Method = Methods.Ping }));

        // If a blank line had produced a response, this would be that response, not id 7.
        var res = await link.ReadResponseAsync();
        Assert.Equal(7, res.Id);
        Assert.True(res.Ok);
    }

    [Fact]
    public async Task UnknownMethod_IsPassedToTheHandler()
    {
        // Deciding that a method is unknown is the handler's job (WorkspaceController); the pipe layer just forwards it.
        await using var client = await ConnectAsync();
        var res = await client.CallAsync("does-not-exist", ct: _cts.Token);
        Assert.True(res.Ok);
        Assert.Equal("does-not-exist", _seen.Single().Method);
    }
}
