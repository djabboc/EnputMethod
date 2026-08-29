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
    private readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _content = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Border _frame;
    private readonly StackPanel _panel;
    private readonly ScrollViewer _scrollViewer;
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
        _scrollViewer = new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _panel = new StackPanel { Children = { _title, _scrollViewer } };
        _frame = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(31, 38, 46)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(97, 111, 126)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = _panel,
        };
        Content = _frame;
    }

    public void ShowTranslation(string clientId, TranslationView view)
    {
        _clientId = clientId;
        OverlayTheme theme = view.Theme is { IsValid: true } configured ? configured : new OverlayTheme();
        ApplyTheme(theme);
        _title.Text = view.Title;
        _content.Text = view.Content;
        if (!IsVisible) Show();
        Point position = OverlayPositioning.Constrain(this, view.CandidateRight + 8, view.CandidateTop);
        Left = position.X;
        Top = position.Y;
    }

    public void HideFor(string clientId)
    {
        if (string.Equals(_clientId, clientId, StringComparison.Ordinal)) Hide();
    }

    private void ApplyTheme(OverlayTheme theme)
    {
        Opacity = theme.Opacity / 255.0;
        _frame.Background = Brush(theme.TranslationBackground, Brushes.Black);
        _frame.BorderBrush = Brush(theme.TranslationBorder, Brushes.Gray);
        _frame.BorderThickness = new Thickness(theme.TranslationBorderWidth);
        _frame.CornerRadius = new CornerRadius(theme.TranslationCornerRadius);
        _frame.Padding = new Thickness(theme.TranslationPadding);
        _panel.Width = theme.TranslationWidth;
        _scrollViewer.MaxHeight = theme.TranslationMaxHeight;
        _title.FontFamily = new FontFamily(theme.FontFamily);
        _title.FontSize = theme.FontSize;
        _title.Foreground = Brush(theme.TranslationTitleForeground, Brushes.White);
        _content.FontFamily = new FontFamily(theme.FontFamily);
        _content.FontSize = theme.FontSize;
        _content.Foreground = Brush(theme.TranslationForeground, Brushes.WhiteSmoke);
    }

    private static Brush Brush(string value, Brush fallback)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
        catch (FormatException) { return fallback; }
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
