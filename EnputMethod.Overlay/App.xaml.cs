using System.IO.Pipes;
using System.Windows;

namespace EnputMethod.Overlay;

public partial class App : Application
{
    private readonly OverlayController _controller = new();
    private Mutex? _instanceMutex;
    private OverlayPipeServer? _pipeServer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string? lifecycleToken = e.Args
            .FirstOrDefault(argument => argument.StartsWith("--lifecycle-test=", StringComparison.Ordinal))?
            ["--lifecycle-test=".Length..];
        if (lifecycleToken is not null && !Guid.TryParseExact(lifecycleToken, "N", out _))
        {
            Shutdown(64);
            return;
        }
        string instanceMutexName = lifecycleToken is null
            ? "Local\\EnputMethod.Overlay.v1"
            : $"Local\\EnputMethod.Overlay.LifecycleTest.{lifecycleToken}";
        string? pipeName = lifecycleToken is null ? null : $"EnputMethod.Overlay.LifecycleTest.{lifecycleToken}";
        bool injectListenerFailure = lifecycleToken is not null && e.Args.Contains("--fail-pipe-listener", StringComparer.Ordinal);

        _instanceMutex = new Mutex(true, instanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }
        Func<string, NamedPipeServerStream>? pipeFactory = injectListenerFailure
            ? _ => throw new UnauthorizedAccessException("Injected Overlay lifecycle-test listener failure.")
            : null;
        _pipeServer = new OverlayPipeServer(_controller.HandleHostMessage, _controller.HandleClientDisconnected, pipeName, pipeFactory);
        _pipeServer.Start();
        _ = MonitorPipeServerAsync(_pipeServer);
        _controller.WarmUp();
    }

    private async Task MonitorPipeServerAsync(OverlayPipeServer server)
    {
        try
        {
            await server.Ready;
            OverlayDiagnostics.Write("pipe.listener-ready");
            await server.Completion;
            if (!server.IsStopping) throw new InvalidOperationException("The Overlay pipe listener stopped unexpectedly.");
        }
        catch (OperationCanceledException) when (server.IsStopping)
        {
        }
        catch (Exception exception)
        {
            if (server.Completion.IsFaulted) _ = server.Completion.Exception;
            OverlayDiagnostics.Write("pipe.listener-failed", $"{exception.GetType().Name}: {exception.Message}");
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
            {
                await Dispatcher.InvokeAsync(() => Shutdown(1));
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeServer?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
