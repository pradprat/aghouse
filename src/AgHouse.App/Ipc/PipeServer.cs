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

    public void Start() => _ = Task.Run(AcceptLoopAsync);

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                var connected = pipe;
                pipe = null;
                _ = Task.Run(() => ServeAsync(connected));
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
                var line = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false);
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

    public void Dispose() => _cts.Cancel();
}
