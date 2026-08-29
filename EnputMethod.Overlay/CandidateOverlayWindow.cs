using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace EnputMethod.Overlay;

internal sealed class CandidateOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
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
        FontFamily candidateFont = new(view.ModeMarker == "EMOJI" ? "Segoe UI Emoji" : theme.FontFamily);
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
                Child = new TextBlock
                {
                    FontFamily = candidateFont,
                    FontSize = theme.FontSize,
                    Foreground = index == view.SelectedIndex ? Brush(theme.SelectedForeground, Brushes.White) : Brush(theme.Foreground, Brushes.White),
                    Text = $"{index + 1}  {view.Items[index]}",
                },
            };
            row.MouseEnter += (_, _) => row.Background = activeBackground;
            row.MouseLeave += (_, _) => row.Background = isSelected ? activeBackground : regularBackground;
            row.MouseLeftButtonUp += (_, _) => _ = sendAction(new OverlayMessage { Type = "selectCandidate", ClientId = clientId, StateId = stateId, CandidateIndex = candidateIndex });
            candidates.Children.Add(row);
        }
        _content.Children.Add(candidates);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
        footer.Children.Add(CreateFooterAction("<", "previousPage", clientId, stateId, sendAction, theme));
        footer.Children.Add(new TextBlock { FontFamily = new FontFamily(theme.FontFamily), FontSize = theme.FontSize, Foreground = Brush(theme.Foreground, Brushes.LightGray), Margin = new Thickness(8, 3, 8, 3), Text = $"{view.Page + 1}/{view.PageCount}" });
        footer.Children.Add(CreateFooterAction(">", "nextPage", clientId, stateId, sendAction, theme));
        if (view.CapsLock) footer.Children.Add(new TextBlock { FontFamily = new FontFamily(theme.FontFamily), FontSize = theme.FontSize, Foreground = Brush(theme.SelectedForeground, Brushes.LightSkyBlue), Margin = new Thickness(8, 3, 0, 3), Text = "CAPS" });
        if (!string.IsNullOrWhiteSpace(view.ModeMarker)) footer.Children.Add(new TextBlock { FontFamily = new FontFamily(theme.FontFamily), FontSize = theme.FontSize, Foreground = Brush(theme.SelectedForeground, Brushes.LightSkyBlue), Margin = new Thickness(8, 3, 0, 3), Text = view.ModeMarker });
        _content.Children.Add(footer);

        if (!IsVisible && OverlayFocus.IsForegroundWindow(_ownerWindow)) Show();
        Point position = OverlayPositioning.Constrain(this, view.X, view.Y);
        Left = position.X;
        Top = position.Y;
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

    private static Border CreateFooterAction(string label, string type, string clientId, long stateId, Func<OverlayMessage, Task> sendAction, OverlayTheme theme)
    {
        var action = new Border
        {
            Background = Brush(theme.Background, Brushes.DimGray),
            Cursor = Cursors.Hand,
            Padding = new Thickness(theme.Padding, 3, theme.Padding, 3),
            Child = new TextBlock { FontFamily = new FontFamily(theme.FontFamily), FontSize = theme.FontSize, Foreground = Brush(theme.Foreground, Brushes.White), Text = label },
        };
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
