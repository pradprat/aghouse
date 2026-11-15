using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AgHouse.Core.Ipc;

namespace AgHouse.App.Ipc;

/// <summary>
/// Newline-delimited JSON-RPC over a named pipe. CurrentUserOnly restricts both the
/// pipe ACL and the client check to the user running aghouse.
/// </summary>
public sealed class PipeServer(string pipeName, Func<RpcRequest, Task<RpcResponse>> handler) : IDisposable
{
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Connections served at once. More wait for a free slot instead of each getting a task and buffers.</summary>
    public const int MaxConnections = 32;

    /// <summary>Longest request line, in chars. send-text of a big file still fits; an endless line doesn't eat memory.</summary>
    public const int MaxLineLength = 4 * 1024 * 1024;

    private readonly SemaphoreSlim _slots = new(MaxConnections, MaxConnections);

    public void Start() => _ = Task.Run(AcceptLoopAsync);

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            var slot = false;
            try
            {
                await _slots.WaitAsync(_cts.Token).ConfigureAwait(false);
                slot = true;
                pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                var connected = pipe;
                pipe = null;
                slot = false; // ServeAsync releases it
                _ = Task.Run(async () =>
                {
                    try { await ServeAsync(connected).ConfigureAwait(false); }
                    finally { _slots.Release(); }
                });
            }
            catch (OperationCanceledException) { break; }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine($"aghouse: pipe error: {ex.Message}");
                await Task.Delay(500).ConfigureAwait(false);
            }
            finally
            {
                pipe?.Dispose();
                if (slot) _slots.Release();
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        await using var _ = pipe;
        var utf8 = new UTF8Encoding(false);
        using var reader = new StreamReader(pipe, utf8, false, 4096, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, utf8, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                string? line;
                try { line = await ReadLineAsync(reader, MaxLineLength, _cts.Token).ConfigureAwait(false); }
                catch (InvalidDataException ex)
                {
                    // The rest of the stream can't be framed any more: answer once and hang up.
                    await writer.WriteLineAsync(RpcJson.Serialize(RpcResponse.Failure(0, ex.Message))).ConfigureAwait(false);
                    break;
                }
                if (line is null) break;
                if (line.Length == 0) continue;

                RpcResponse response;
                RpcRequest? request = null;
                try
                {
                    request = RpcJson.Deserialize<RpcRequest>(line);
                    response = request is null
                        ? RpcResponse.Failure(0, "Empty request.")
                        : await handler(request).ConfigureAwait(false);
                }
                catch (JsonException ex) { response = RpcResponse.Failure(0, $"Malformed request: {ex.Message}"); }
                catch (Exception ex) { response = RpcResponse.Failure(request?.Id ?? 0, ex.Message); }

                await writer.WriteLineAsync(RpcJson.Serialize(response)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { /* client went away */ }
    }

    /// <summary>
    /// Like <see cref="StreamReader.ReadLineAsync(CancellationToken)"/> (LF or CRLF ends a line; null at
    /// the end of the stream), but a line longer than <paramref name="max"/> chars throws
    /// <see cref="InvalidDataException"/> instead of growing without bound.
    /// </summary>
    internal static async Task<string?> ReadLineAsync(StreamReader reader, int max, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var one = new char[1];
        while (true)
        {
            // StreamReader buffers, so reading one char at a time doesn't mean one pipe read per char.
            var n = await reader.ReadAsync(one.AsMemory(), ct).ConfigureAwait(false);
            if (n == 0) return sb.Length > 0 ? sb.ToString() : null;
            var c = one[0];
            if (c == '\n')
            {
                if (sb.Length > 0 && sb[^1] == '\r') sb.Length--;
                return sb.ToString();
            }
            if (sb.Length >= max) throw new InvalidDataException($"Request longer than {max} characters.");
            sb.Append(c);
        }
    }

    public void Dispose() => _cts.Cancel();
}
