using System.Collections.Concurrent;
using System.IO.Pipes;

namespace StartDX.Shared.Ipc;

public sealed class IpcSession
{
    internal IpcSession(MessageChannel channel) => Channel = channel;
    internal MessageChannel Channel { get; }
    public Guid Id { get; } = Guid.NewGuid();
    public Task SendAsync(IpcEnvelope envelope) => Channel.SendAsync(envelope);
}

/// <summary>
/// Multi-client pipe server hosted by the dock. Events fire on thread-pool threads;
/// the host is responsible for marshalling to its UI thread.
/// </summary>
public sealed class IpcServer : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<Guid, IpcSession> _sessions = new();
    private Task? _acceptLoop;

    public event Action<IpcSession>? ClientConnected;
    public event Action<IpcSession>? ClientDisconnected;
    public event Action<IpcSession, IpcEnvelope>? MessageReceived;

    public int ClientCount => _sessions.Count;

    public void Start() => _acceptLoop ??= Task.Run(AcceptLoopAsync);

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    IpcProtocol.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                var accepted = pipe;
                pipe = null; // ownership moves to the session task
                _ = Task.Run(() => RunSessionAsync(accepted));
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                return;
            }
            catch (Exception)
            {
                pipe?.Dispose();
                try { await Task.Delay(500, _cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task RunSessionAsync(NamedPipeServerStream pipe)
    {
        using var channel = new MessageChannel(pipe);
        var session = new IpcSession(channel);
        _sessions[session.Id] = session;
        ClientConnected?.Invoke(session);
        try
        {
            while (await channel.ReadAsync(_cts.Token).ConfigureAwait(false) is { } env)
                MessageReceived?.Invoke(session, env);
        }
        catch (Exception) { /* disconnect / cancellation / bad peer: drop this session only */ }
        finally
        {
            _sessions.TryRemove(session.Id, out _);
            ClientDisconnected?.Invoke(session);
            pipe.Dispose();
        }
    }

    public void Broadcast(IpcEnvelope envelope)
    {
        foreach (var s in _sessions.Values)
            _ = SafeSend(s, envelope);
    }

    private static async Task SafeSend(IpcSession s, IpcEnvelope e)
    {
        try { await s.SendAsync(e).ConfigureAwait(false); }
        catch (Exception) { /* the read loop will notice the dead pipe and clean up */ }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
