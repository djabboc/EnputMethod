using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EnputMethod.Overlay;

internal sealed class CandidateOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const double FooterButtonWidth = 28;
    private const double FooterPageWidth = 52;
    private readonly StackPanel _content = new();
    private readonly Border _frame;
    private string? _clientId;
    private long _stateId;
    private Func<OverlayMessage, Task>? _sendAction;
    private long _ownerWindow;
    private bool _hasCandidates;

    public CandidateOverlayWindow()
    {
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Focusable = false;
        ShowActivated = false;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        WindowStyle = WindowStyle.None;
        _frame = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(31, 38, 46)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(97, 111, 126)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            Child = _content,
        };
        _frame.MouseRightButtonUp += (_, _) =>
        {
            if (_clientId is not null && _sendAction is not null)
            {
                _ = _sendAction(new OverlayMessage { Type = "dismiss", ClientId = _clientId, StateId = _stateId });
            }
        };
        Content = _frame;
    }

    public void ShowCandidates(string clientId, long stateId, CandidateView view, Func<OverlayMessage, Task> sendAction)
    {
        _clientId = clientId;
        _stateId = stateId;
        _sendAction = sendAction;
        _ownerWindow = view.OwnerWindow;
        _hasCandidates = true;
        OverlayTheme theme = view.Theme is { IsValid: true } configured ? configured : new OverlayTheme();
        ApplyTheme(theme);
        bool emojiMode = view.ModeMarker == "EMOJI";
        FontFamily candidateFont = new(emojiMode ? "Segoe UI Emoji" : theme.FontFamily);
        _content.Children.Clear();
        var candidates = new StackPanel { Orientation = view.Layout == "horizontal" ? Orientation.Horizontal : Orientation.Vertical };
        for (int index = 0; index < view.Items.Count; ++index)
        {
            int candidateIndex = index;
            Brush regularBackground = Brushes.Transparent;
            Brush activeBackground = Brush(theme.SelectedBackground, Brushes.SteelBlue);
            bool isSelected = index == view.SelectedIndex;
            var row = new Border
            {
                Background = isSelected ? activeBackground : regularBackground,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 1, view.Layout == "horizontal" ? 6 : 0, 1),
                MinHeight = theme.RowHeight,
                Padding = new Thickness(theme.Padding, 3, theme.Padding, 3),
                Child = CreateCandidateContent(index, view.Items[index], emojiMode, candidateFont, theme, isSelected),
            };
            row.MouseEnter += (_, _) => row.Background = activeBackground;
            row.MouseLeave += (_, _) => row.Background = isSelected ? activeBackground : regularBackground;
            row.MouseLeftButtonUp += (_, _) => _ = sendAction(new OverlayMessage { Type = "selectCandidate", ClientId = clientId, StateId = stateId, CandidateIndex = candidateIndex });
            candidates.Children.Add(row);
        }
        _content.Children.Add(candidates);

        // Keep paging controls on the leading edge so long candidates expand only to the right.
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        var pager = new Grid { Width = FooterButtonWidth * 2 + FooterPageWidth, Height = Math.Max(22, theme.RowHeight - 6) };
        pager.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(FooterButtonWidth) });
        pager.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(FooterPageWidth) });
        pager.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(FooterButtonWidth) });
        var previous = CreateFooterAction("<", "previousPage", "Previous page", view.Page > 0, clientId, stateId, sendAction, theme);
        Grid.SetColumn(previous, 0);
        pager.Children.Add(previous);
        var page = new TextBlock
        {
            FontFamily = new FontFamily(theme.FontFamily),
            FontSize = theme.FontSize,
            Foreground = Brush(theme.Foreground, Brushes.LightGray),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Text = $"{view.Page + 1}/{view.PageCount}",
        };
        Grid.SetColumn(page, 1);
        pager.Children.Add(page);
        var next = CreateFooterAction(">", "nextPage", "Next page", view.Page + 1 < view.PageCount, clientId, stateId, sendAction, theme);
        Grid.SetColumn(next, 2);
        pager.Children.Add(next);
        footer.Children.Add(pager);
        if (view.CapsLock) footer.Children.Add(new TextBlock { FontFamily = new FontFamily(theme.FontFamily), FontSize = theme.FontSize, Foreground = Brush(theme.SelectedForeground, Brushes.LightSkyBlue), Margin = new Thickness(8, 3, 0, 3), Text = "CAPS" });
        if (!string.IsNullOrWhiteSpace(view.ModeMarker)) footer.Children.Add(new TextBlock { FontFamily = new FontFamily(theme.FontFamily), FontSize = theme.FontSize, Foreground = Brush(theme.SelectedForeground, Brushes.LightSkyBlue), Margin = new Thickness(8, 3, 0, 3), Text = view.ModeMarker });
        _content.Children.Add(footer);

        if (!IsVisible && OverlayFocus.IsForegroundWindow(_ownerWindow)) Show();
        Point position = OverlayPositioning.Constrain(this, view.X, view.Y);
        Left = position.X;
        Top = position.Y;
    }

    internal Rect? ScreenBoundsFor(string clientId)
    {
        if (!string.Equals(_clientId, clientId, StringComparison.Ordinal) || !_hasCandidates) return null;
        return OverlayPositioning.ScreenBounds(this);
    }

    public void HideFor(string clientId)
    {
        if (!string.Equals(_clientId, clientId, StringComparison.Ordinal)) return;
        _hasCandidates = false;
        Hide();
    }

    public void RefreshForegroundVisibility()
    {
        if (!_hasCandidates) return;
        if (OverlayFocus.IsForegroundWindow(_ownerWindow))
        {
            if (!IsVisible) Show();
        }
        else if (IsVisible) Hide();
    }

    private void ApplyTheme(OverlayTheme theme)
    {
        Opacity = theme.Opacity / 255.0;
        _frame.Background = Brush(theme.Background, Brushes.Black);
        _frame.BorderBrush = Brush(theme.Border, Brushes.Gray);
        _frame.BorderThickness = new Thickness(theme.BorderWidth);
        _frame.CornerRadius = new CornerRadius(theme.CornerRadius);
        _frame.Padding = new Thickness(theme.Padding);
    }

    private static FrameworkElement CreateCandidateContent(int index, string candidate, bool emojiMode, FontFamily font, OverlayTheme theme, bool isSelected)
    {
        Brush foreground = isSelected ? Brush(theme.SelectedForeground, Brushes.White) : Brush(theme.Foreground, Brushes.White);
        if (!emojiMode) return new TextBlock
        {
            FontFamily = font,
            FontSize = theme.FontSize,
            Foreground = foreground,
            Text = $"{index + 1}  {candidate}",
        };

        string emoji = EmojiAssetResolver.EmojiFromCandidate(candidate);
        string label = EmojiAssetResolver.LabelFromCandidate(candidate);
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock
        {
            FontFamily = new FontFamily(theme.FontFamily),
            FontSize = theme.FontSize,
            Foreground = foreground,
            Text = $"{index + 1}",
            Width = Math.Ceiling(theme.FontSize * 1.8),
            VerticalAlignment = VerticalAlignment.Center,
        });

        BitmapImage? image = EmojiAssetResolver.Load(emoji);
        if (image is not null)
        {
            content.Children.Add(new Image
            {
                Source = image,
                Width = Math.Max(20, theme.RowHeight - 6),
                Height = Math.Max(20, theme.RowHeight - 6),
                Margin = new Thickness(0, 0, 6, 0),
                Stretch = Stretch.Uniform,
            });
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                FontFamily = font,
                FontSize = theme.FontSize,
                Foreground = foreground,
                Text = emoji,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        content.Children.Add(new TextBlock
        {
            FontFamily = new FontFamily(theme.FontFamily),
            FontSize = theme.FontSize,
            Foreground = foreground,
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return content;
    }
    private static Border CreateFooterAction(string label, string type, string tooltip, bool enabled, string clientId, long stateId, Func<OverlayMessage, Task> sendAction, OverlayTheme theme)
    {
        Brush regularBackground = Brushes.Transparent;
        Brush hoverBackground = Brush(theme.SelectedBackground, Brushes.SteelBlue);
        var action = new Border
        {
            Background = regularBackground,
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
            Width = FooterButtonWidth,
            Padding = new Thickness(2),
            Opacity = enabled ? 1 : 0.45,
            ToolTip = tooltip,
            Child = new TextBlock
            {
                FontFamily = new FontFamily(theme.FontFamily),
                FontSize = theme.FontSize,
                Foreground = Brush(theme.Foreground, Brushes.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Text = label,
            },
        };
        if (!enabled) return action;
        action.MouseEnter += (_, _) => action.Background = hoverBackground;
        action.MouseLeave += (_, _) => action.Background = regularBackground;
        action.MouseLeftButtonUp += (_, _) => _ = sendAction(new OverlayMessage { Type = type, ClientId = clientId, StateId = stateId });
        return action;
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

    private static IntPtr WindowProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmMouseActivate) return IntPtr.Zero;
        handled = true;
        return new IntPtr(MaNoActivate);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

}
