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
        _instanceMutex = new Mutex(true, "Local\\EnputMethod.Overlay.v1", out bool createdNew);
        if (!createdNew)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }
        _pipeServer = new OverlayPipeServer(_controller.HandleHostMessage, _controller.HandleClientDisconnected);
        _pipeServer.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeServer?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
