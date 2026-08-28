using System.Windows;

namespace EnputMethod.Overlay;

public partial class App : Application
{
    private readonly OverlayController _controller = new();
    private OverlayPipeServer? _pipeServer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _pipeServer = new OverlayPipeServer(_controller.HandleHostMessage);
        _pipeServer.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeServer?.Dispose();
        base.OnExit(e);
    }
}
