using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace EnputMethod.Overlay;

internal sealed class OverlayPipeServer : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Action<OverlayMessage, Func<OverlayMessage, Task>> _messageHandler;
    private readonly SemaphoreSlim _writerLock = new(1, 1);
    private Task? _listener;
    private StreamWriter? _writer;

    public OverlayPipeServer(Action<OverlayMessage, Func<OverlayMessage, Task>> messageHandler)
    {
        _messageHandler = messageHandler;
    }

    public void Start()
    {
        _listener ??= Task.Run(ListenAsync);
    }

    private async Task ListenAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    OverlayProtocol.PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(_cancellation.Token);
                await ServeConnectionAsync(pipe, _cancellation.Token);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                // A disconnected Host is expected; immediately accept a replacement connection.
            }
        }
    }

    private async Task ServeConnectionAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        _writer = writer;

        try
        {
            await SendRawAsync(JsonSerializer.Serialize(new { type = "ready", protocol = 1 }, OverlayProtocol.JsonOptions));
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(cancellationToken);
                if (line is null) return;
                if (!OverlayProtocol.TryParse(line, out OverlayMessage? message) || message is null)
                {
                    await SendRawAsync(JsonSerializer.Serialize(new { type = "error", reason = "invalidMessage" }, OverlayProtocol.JsonOptions));
                    continue;
                }

                _messageHandler(message, SendActionAsync);
                await SendRawAsync(JsonSerializer.Serialize(new { type = "accepted", stateId = message.StateId }, OverlayProtocol.JsonOptions));
            }
        }
        finally
        {
            if (ReferenceEquals(_writer, writer)) _writer = null;
        }
    }

    private Task SendActionAsync(OverlayMessage message)
    {
        return SendRawAsync(JsonSerializer.Serialize(message, OverlayProtocol.JsonOptions));
    }

    private async Task SendRawAsync(string message)
    {
        await _writerLock.WaitAsync(_cancellation.Token);
        try
        {
            if (_writer is not null) await _writer.WriteLineAsync(message);
        }
        catch (IOException)
        {
            // The Host may exit between a click and the response.
        }
        finally
        {
            _writerLock.Release();
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try { _listener?.Wait(TimeSpan.FromSeconds(1)); }
        catch (AggregateException) { }
        _writerLock.Dispose();
        _cancellation.Dispose();
    }
}
