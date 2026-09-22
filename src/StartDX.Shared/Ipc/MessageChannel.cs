using System.Text;
using System.Text.Json;

namespace StartDX.Shared.Ipc;

/// <summary>Line-framed JSON message pump over any duplex <see cref="Stream"/>.</summary>
public sealed class MessageChannel : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public MessageChannel(Stream stream)
    {
        _reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        _writer = new StreamWriter(stream, Utf8, bufferSize: 4096, leaveOpen: true) { NewLine = "\n", AutoFlush = false };
    }

    /// <summary>Returns null on clean end-of-stream. Malformed lines are skipped, not fatal.</summary>
    public async Task<IpcEnvelope?> ReadAsync(CancellationToken ct)
    {
        while (true)
        {
            var line = await _reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) return null;
            if (line.Length == 0) continue;
            if (line.Length > IpcProtocol.MaxMessageChars)
                throw new InvalidDataException("IPC message exceeds the maximum allowed size.");
            try
            {
                var env = JsonSerializer.Deserialize<IpcEnvelope>(line, IpcProtocol.Json);
                if (env is { V: IpcProtocol.Version } && !string.IsNullOrEmpty(env.Type)) return env;
            }
            catch (JsonException)
            {
                // Ignore garbage; a misbehaving peer must not be able to take the dock down.
            }
        }
    }

    public async Task SendAsync(IpcEnvelope envelope, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(envelope, IpcProtocol.Json);
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
            await _writer.FlushAsync(ct).ConfigureAwait(false);
        }
        finally { _writeLock.Release(); }
    }

    public void Dispose()
    {
        try { _writer.Dispose(); } catch { /* stream may already be broken */ }
        _reader.Dispose();
        _writeLock.Dispose();
    }
}
