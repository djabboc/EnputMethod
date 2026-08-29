using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace EnputMethod.Overlay;

internal sealed class TranslationOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;
    private const int WmMouseActivate = 0x0021;
    private const int WmNcHitTest = 0x0084;
    private const int MaNoActivate = 3;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int ResizeBorderPixels = 8;
    private readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold };
    private readonly RichTextBox _content = new()
    {
        IsReadOnly = true,
        IsDocumentEnabled = false,
        Focusable = false,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = new Thickness(0),
    };
    private readonly Border _frame;
    private readonly Grid _panel;
    private readonly DispatcherTimer _saveSizeTimer;
    private string? _clientId;
    private long _ownerWindow;
    private bool _hasTranslation;
    private bool _applyingTheme;
    private bool _hasUserSized;

    public TranslationOverlayWindow()
    {
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Focusable = false;
        ShowActivated = false;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.Manual;
        ResizeMode = ResizeMode.CanResize;
        MinWidth = 260;
        MinHeight = 160;
        MaxWidth = 1200;
        MaxHeight = 900;
        Width = 380;
        Height = 280;
        Topmost = true;
        WindowStyle = WindowStyle.None;
        _content.Document = new FlowDocument { PagePadding = new Thickness(0) };
        _panel = new Grid();
        _panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_title, 0);
        Grid.SetRow(_content, 1);
        _panel.Children.Add(_title);
        _panel.Children.Add(_content);
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
        _saveSizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _saveSizeTimer.Tick += (_, _) =>
        {
            _saveSizeTimer.Stop();
            PersistSize();
        };
        SizeChanged += OnSizeChanged;
        Closed += (_, _) => _saveSizeTimer.Stop();
    }

    public void ShowTranslation(string clientId, TranslationView view, Rect? candidateBounds)
    {
        _clientId = clientId;
        _ownerWindow = view.OwnerWindow;
        _hasTranslation = true;
        OverlayTheme theme = view.Theme is { IsValid: true } configured ? configured : new OverlayTheme();
        ApplyTheme(theme);
        _title.Text = view.Title;
        RenderContent(view.Content, theme);
        if (!IsVisible && OverlayFocus.IsForegroundWindow(_ownerWindow)) Show();
        Point position = candidateBounds is Rect bounds
            ? OverlayPositioning.Adjacent(this, bounds)
            : OverlayPositioning.Constrain(this, view.CandidateRight + 8, view.CandidateTop);
        Left = position.X;
        Top = position.Y;
    }

    public void HideFor(string clientId)
    {
        if (!string.Equals(_clientId, clientId, StringComparison.Ordinal)) return;
        _hasTranslation = false;
        Hide();
    }

    public void RefreshForegroundVisibility()
    {
        if (!_hasTranslation) return;
        if (OverlayFocus.IsForegroundWindow(_ownerWindow))
        {
            if (!IsVisible) Show();
        }
        else if (IsVisible) Hide();
    }

    private void ApplyTheme(OverlayTheme theme)
    {
        _applyingTheme = true;
        try
        {
            Opacity = theme.Opacity / 255.0;
            _frame.Background = Brush(theme.TranslationBackground, Brushes.Black);
            _frame.BorderBrush = Brush(theme.TranslationBorder, Brushes.Gray);
            _frame.BorderThickness = new Thickness(theme.TranslationBorderWidth);
            _frame.CornerRadius = new CornerRadius(theme.TranslationCornerRadius);
            _frame.Padding = new Thickness(theme.TranslationPadding);
            if (!_hasUserSized)
            {
                Width = theme.TranslationWindowWidth;
                Height = theme.TranslationWindowHeight;
            }
            _title.FontFamily = new FontFamily(theme.FontFamily);
            _title.FontSize = theme.WpfFontSize;
            _title.Foreground = Brush(theme.TranslationTitleForeground, Brushes.White);
            _content.FontFamily = new FontFamily(theme.FontFamily);
            _content.FontSize = theme.WpfFontSize;
            _content.Foreground = Brush(theme.TranslationForeground, Brushes.WhiteSmoke);
            _content.Document.PageWidth = Math.Max(1, Width - (_frame.Padding.Left + _frame.Padding.Right + _frame.BorderThickness.Left + _frame.BorderThickness.Right));
        }
        finally
        {
            _applyingTheme = false;
        }
    }

    private void RenderContent(string content, OverlayTheme theme)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily(theme.FontFamily),
            FontSize = theme.WpfFontSize,
            Foreground = Brush(theme.TranslationForeground, Brushes.WhiteSmoke),
            PagePadding = new Thickness(0),
            PageWidth = Math.Max(1, Width - (_frame.Padding.Left + _frame.Padding.Right + _frame.BorderThickness.Left + _frame.BorderThickness.Right)),
        };
        bool firstLine = true;
        foreach (string rawLine in content.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, firstLine ? 6 : 3, 0, 0) };
            if (rawLine.StartsWith("Example: ", StringComparison.Ordinal))
            {
                paragraph.Inlines.Add(new Run("Example: ") { FontWeight = FontWeights.SemiBold });
                paragraph.Inlines.Add(new Run(rawLine["Example: ".Length..]) { FontStyle = FontStyles.Italic });
            }
            else if (TrySplitLanguageLabel(rawLine, out string? label, out string? meaning))
            {
                paragraph.Inlines.Add(new Run($"{label}: ") { FontWeight = FontWeights.SemiBold });
                paragraph.Inlines.Add(new Run(meaning));
            }
            else
            {
                paragraph.Inlines.Add(new Run(rawLine) { FontWeight = firstLine ? FontWeights.SemiBold : FontWeights.Normal });
            }
            document.Blocks.Add(paragraph);
            firstLine = false;
        }
        _content.Document = document;
    }

    private static bool TrySplitLanguageLabel(string line, out string? label, out string? meaning)
    {
        label = null;
        meaning = null;
        int separator = line.IndexOf(": ", StringComparison.Ordinal);
        if (separator is <= 0 or > 16) return false;
        string candidate = line[..separator];
        if (candidate.Any(character => !char.IsLetterOrDigit(character) && character != '-')) return false;
        label = candidate;
        meaning = line[(separator + 2)..];
        return true;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        if (_applyingTheme || !IsVisible || eventArgs.NewSize == eventArgs.PreviousSize) return;
        _hasUserSized = true;
        _saveSizeTimer.Stop();
        _saveSizeTimer.Start();
    }

    private void PersistSize()
    {
        if (Width < MinWidth || Height < MinHeight) return;
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Enput Method");
            Directory.CreateDirectory(directory);
            string configurationPath = Path.Combine(directory, "config.json");
            JsonObject configuration = File.Exists(configurationPath)
                ? JsonNode.Parse(File.ReadAllText(configurationPath)) as JsonObject ?? new JsonObject()
                : new JsonObject();
            configuration["translationWindowWidth"] = (int)Math.Round(Width);
            configuration["translationWindowHeight"] = (int)Math.Round(Height);
            string pendingPath = configurationPath + ".overlay.pending";
            File.WriteAllText(pendingPath, configuration.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(pendingPath, configurationPath, true);
        }
        catch (JsonException)
        {
            // A malformed user configuration must not be overwritten by a UI resize.
        }
        catch (IOException)
        {
            // The input service can read the configuration while the user resizes.
        }
        catch (UnauthorizedAccessException)
        {
            // A restricted profile can still use the current session size.
        }
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
        ((HwndSource)PresentationSource.FromVisual(this)).AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmMouseActivate)
        {
            handled = true;
            return new IntPtr(MaNoActivate);
        }
        if (message != WmNcHitTest || !GetWindowRect(window, out NativeRect bounds)) return IntPtr.Zero;

        int x = unchecked((short)(long)lParam);
        int y = unchecked((short)((long)lParam >> 16));
        bool left = x - bounds.Left <= ResizeBorderPixels;
        bool right = bounds.Right - x <= ResizeBorderPixels;
        bool top = y - bounds.Top <= ResizeBorderPixels;
        bool bottom = bounds.Bottom - y <= ResizeBorderPixels;
        int hit = (left, right, top, bottom) switch
        {
            (true, _, true, _) => HtTopLeft,
            (_, true, true, _) => HtTopRight,
            (true, _, _, true) => HtBottomLeft,
            (_, true, _, true) => HtBottomRight,
            (true, _, _, _) => HtLeft,
            (_, true, _, _) => HtRight,
            (_, _, true, _) => HtTop,
            (_, _, _, true) => HtBottom,
            _ => 0,
        };
        if (hit == 0) return IntPtr.Zero;
        handled = true;
        return new IntPtr(hit);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
}
