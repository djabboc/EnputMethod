using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace EnputMethod.Overlay.TestHost;

public partial class MainWindow : Window
{
    private const string PipeName = "EnputMethod.Overlay.v1";
    private readonly string _clientId = $"test-host-{Guid.NewGuid():N}";
    private readonly CancellationTokenSource _cancellation = new();
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private long _stateId;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        ConnectionStatus.Text = "Connecting to Overlay...";
        try
        {
            _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await _pipe.ConnectAsync(5000, _cancellation.Token);
            _reader = new StreamReader(_pipe, new UTF8Encoding(false), false, 4096, true);
            _writer = new StreamWriter(_pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            ConnectionStatus.Text = "Connected";
            _ = ReadEventsAsync();
        }
        catch (Exception exception) when (!_cancellation.IsCancellationRequested)
        {
            ConnectionStatus.Text = "Overlay unavailable";
            AppendEvent(exception.Message);
        }
    }

    private async Task ReadEventsAsync()
    {
        try
        {
            while (!_cancellation.IsCancellationRequested && _reader is not null)
            {
                string? line = await _reader.ReadLineAsync(_cancellation.Token);
                if (line is null) return;
                await Dispatcher.InvokeAsync(() => AppendEvent(line));
            }
        }
        catch (IOException) when (!_cancellation.IsCancellationRequested)
        {
            await Dispatcher.InvokeAsync(() => ConnectionStatus.Text = "Overlay disconnected");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void ShowCandidates_Click(object sender, RoutedEventArgs e)
    {
        await SendCandidatesAsync();
    }

    private async Task SendCandidatesAsync()
    {
        Point origin = PointToScreen(new Point(Width - 210, 80));
        long stateId = ++_stateId;
        await SendAsync(new
        {
            type = "showCandidates",
            clientId = _clientId,
            stateId,
            candidates = new
            {
                x = (int)origin.X,
                y = (int)origin.Y,
                items = new[] { "hello", "help", "helium", "hero" },
                page = 0,
                pageCount = 1,
                selectedIndex = 0,
                layout = "vertical",
                theme = Theme(),
            },
        });
    }

    private async void ShowTranslation_Click(object sender, RoutedEventArgs e)
    {
        if (_stateId == 0) await SendCandidatesAsync();
        await SendAsync(new
        {
            type = "showTranslation",
            clientId = _clientId,
            stateId = _stateId,
            translation = new
            {
                title = "hello",
                content = "interjection\nUsed as a greeting.\nExample: Hello, world!",
                candidateRight = (int)(Left + Width),
                candidateTop = (int)(Top + 80),
                theme = Theme(),
            },
        });
    }

    private async void Hide_Click(object sender, RoutedEventArgs e)
    {
        await SendAsync(new { type = "hide", clientId = _clientId, stateId = ++_stateId });
    }

    private async Task SendAsync(object message)
    {
        if (_writer is null)
        {
            AppendEvent("Not connected");
            return;
        }
        await _writer.WriteLineAsync(JsonSerializer.Serialize(message));
    }

    private static object Theme() => new
    {
        background = "#1f2937",
        foreground = "#f3f4f6",
        border = "#4b5563",
        selectedBackground = "#374151",
        selectedForeground = "#ffffff",
        translationBackground = "#1f2937",
        translationForeground = "#f3f4f6",
        translationTitleForeground = "#ffffff",
        translationBorder = "#4b5563",
        fontFamily = "Segoe UI",
        fontSize = 16,
        opacity = 255,
        borderWidth = 1,
        cornerRadius = 8,
        padding = 10,
        rowHeight = 28,
        translationBorderWidth = 1,
        translationCornerRadius = 8,
        translationPadding = 10,
        translationWidth = 380,
        translationMaxHeight = 240,
    };

    private void AppendEvent(string text) => Events.AppendText($"{DateTime.Now:HH:mm:ss}  {text}{Environment.NewLine}");

    private void Window_Closed(object? sender, EventArgs e)
    {
        _cancellation.Cancel();
        _writer?.Dispose();
        _reader?.Dispose();
        _pipe?.Dispose();
        _cancellation.Dispose();
    }
}
