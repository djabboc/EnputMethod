using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace EnputMethod.Overlay;

internal sealed class TranslationOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;
    private readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
    private readonly TextBlock _content = new() { Foreground = Brushes.WhiteSmoke, TextWrapping = TextWrapping.Wrap };
    private string? _clientId;

    public TranslationOverlayWindow()
    {
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Focusable = false;
        ShowActivated = false;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        WindowStyle = WindowStyle.None;
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(31, 38, 46)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(97, 111, 126)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = new StackPanel
            {
                Width = 280,
                Children =
                {
                    _title,
                    new ScrollViewer { Content = _content, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                },
            },
        };
    }

    public void ShowTranslation(string clientId, TranslationView view)
    {
        _clientId = clientId;
        _title.Text = view.Title;
        _content.Text = view.Content;
        Left = view.CandidateRight + 8;
        Top = view.CandidateTop;
        if (!IsVisible) Show();
    }

    public void HideFor(string clientId)
    {
        if (string.Equals(_clientId, clientId, StringComparison.Ordinal)) Hide();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr handle = new WindowInteropHelper(this).Handle;
        long style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExNoActivate | WsExToolWindow));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
