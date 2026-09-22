using System.IO.Pipes;

namespace StartDX.Shared.Ipc;

/// <summary>
/// Auto-reconnecting pipe client. The settings app can start before, after, or be restarted
/// independently of the dock; the client simply keeps retrying and re-requests state on connect.
/// </summary>
public sealed class IpcClient : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private volatile MessageChannel? _channel;
    private Task? _loop;

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<IpcEnvelope>? MessageReceived;

    public bool IsConnected => _channel is not null;

    public void Start() => _loop ??= Task.Run(RunAsync);

    private async Task RunAsync()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            var wasConnected = false;
            try
            {
                using var pipe = new NamedPipeClientStream(".", IpcProtocol.PipeName, PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(1000, ct).ConfigureAwait(false);

                using var channel = new MessageChannel(pipe);
                _channel = channel;
                wasConnected = true;
                Connected?.Invoke();
                await channel.SendAsync(IpcEnvelope.Create(IpcProtocol.Types.GetState), ct).ConfigureAwait(false);

                while (await channel.ReadAsync(ct).ConfigureAwait(false) is { } env)
                    MessageReceived?.Invoke(env);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception) { /* dock not running / pipe broke: fall through to retry */ }
            finally
            {
                _channel = null;
                if (wasConnected) Disconnected?.Invoke();
            }

            try { await Task.Delay(1000, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Returns false (instead of throwing) when the dock is not connected.</summary>
    public async Task<bool> SendAsync(IpcEnvelope envelope)
    {
        var ch = _channel;
        if (ch is null) return false;
        try { await ch.SendAsync(envelope, _cts.Token).ConfigureAwait(false); return true; }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
