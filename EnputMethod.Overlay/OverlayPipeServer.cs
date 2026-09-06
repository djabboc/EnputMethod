using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace EnputMethod.Overlay;

internal sealed class OverlayPipeServer : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ConcurrentDictionary<string, PipeConnection> _connections = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<OverlayMessage, Func<OverlayMessage, Task>> _messageHandler;
    private readonly Action<string>? _clientDisconnected;
    private readonly Func<string, NamedPipeServerStream> _pipeFactory;
    private readonly string _pipeName;
    private Task? _listener;

    public OverlayPipeServer(
        Action<OverlayMessage, Func<OverlayMessage, Task>> messageHandler,
        Action<string>? clientDisconnected = null,
        string? pipeName = null,
        Func<string, NamedPipeServerStream>? pipeFactory = null)
    {
        _messageHandler = messageHandler;
        _clientDisconnected = clientDisconnected;
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? OverlayProtocol.PipeName : pipeName;
        _pipeFactory = pipeFactory ?? CreatePipe;
    }

    public Task Ready => _ready.Task;
    public Task Completion => _listener ?? Task.CompletedTask;
    public bool IsStopping => _cancellation.IsCancellationRequested;

    public void Start()
    {
        _listener ??= Task.Run(ListenAsync);
    }

    private async Task ListenAsync()
    {
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = _pipeFactory(_pipeName);
                    _ready.TrySetResult();
                    await pipe.WaitForConnectionAsync(_cancellation.Token);
                    NamedPipeServerStream connectedPipe = pipe;
                    pipe = null;
                    _ = Task.Run(() => ServeConnectionAsync(connectedPipe, _cancellation.Token));
                }
                catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (IOException)
                {
                    // A failed connection must not prevent the next TSF Host from connecting.
                    if (!_cancellation.IsCancellationRequested) await Task.Delay(100, _cancellation.Token);
                }
                finally
                {
                    pipe?.Dispose();
                }
            }
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
            throw;
        }
        finally
        {
            if (!_ready.Task.IsCompleted) _ready.TrySetCanceled(_cancellation.Token);
        }
    }

    private static NamedPipeServerStream CreatePipe(string pipeName) => new(
        pipeName,
        PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);

    private async Task ServeConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using (pipe)
        {
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        PipeConnection? connection = null;

        try
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "ready", protocol = 1 }, OverlayProtocol.JsonOptions));
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(cancellationToken);
                if (line is null) return;
                if (!OverlayProtocol.TryParse(line, out OverlayMessage? message) || message is null)
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "error", reason = "invalidMessage" }, OverlayProtocol.JsonOptions));
                    continue;
                }

                if (connection is null)
                {
                    connection = new PipeConnection(message.ClientId, writer);
                    _connections.AddOrUpdate(message.ClientId, connection, (_, _) => connection);
                    OverlayDiagnostics.Write("pipe.connected", message.ClientId);
                }
                else if (!string.Equals(connection.ClientId, message.ClientId, StringComparison.Ordinal))
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "error", reason = "clientIdChanged" }, OverlayProtocol.JsonOptions));
                    continue;
                }

                OverlayDiagnostics.Write("pipe.received", $"{message.Type} state={message.StateId} client={message.ClientId}");
                if (message.Candidates is not null) OverlayDiagnostics.Write("candidate.view", $"state={message.StateId} client={message.ClientId} fontSize={message.Candidates.Theme?.FontSize} emojiMode={message.Candidates.ModeMarker == "EMOJI"} page={message.Candidates.Page + 1}/{message.Candidates.PageCount}");
                _messageHandler(message, SendActionAsync);
            }
        }
        catch (IOException)
        {
            // A Host process can exit while its service object still owns the pipe.
        }
        finally
        {
            if (connection is not null && _connections.TryGetValue(connection.ClientId, out PipeConnection? current) && ReferenceEquals(current, connection))
            {
                _connections.TryRemove(connection.ClientId, out _);
                OverlayDiagnostics.Write("pipe.disconnected", connection.ClientId);
            }
            if (connection is not null) _clientDisconnected?.Invoke(connection.ClientId);
        }
        }
    }

    private Task SendActionAsync(OverlayMessage message)
    {
        if (!_connections.TryGetValue(message.ClientId, out PipeConnection? connection)) return Task.CompletedTask;
        return connection.SendAsync(JsonSerializer.Serialize(message, OverlayProtocol.JsonOptions), _cancellation.Token);
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        if (!_ready.Task.IsCompleted) _ready.TrySetCanceled(_cancellation.Token);
        try { _listener?.Wait(TimeSpan.FromSeconds(1)); }
        catch (AggregateException) { }
        foreach (PipeConnection connection in _connections.Values) connection.Dispose();
        _cancellation.Dispose();
    }

    private sealed class PipeConnection(string clientId, StreamWriter writer) : IDisposable
    {
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public string ClientId { get; } = clientId;

        public async Task SendAsync(string message, CancellationToken cancellationToken)
        {
            var entered = false;
            try
            {
                await _writeLock.WaitAsync(cancellationToken);
                entered = true;
                await writer.WriteLineAsync(message);
            }
            catch (IOException)
            {
                // The caller will reconnect with a new pipe instance.
            }
            finally
            {
                if (entered) _writeLock.Release();
            }
        }

        public void Dispose() => _writeLock.Dispose();
    }
}
