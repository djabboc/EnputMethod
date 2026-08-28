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
    private readonly StackPanel _content = new();

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
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(31, 38, 46)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(97, 111, 126)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            Child = _content,
        };
    }

    public void ShowCandidates(string clientId, long stateId, CandidateView view, Func<OverlayMessage, Task> sendAction)
    {
        _content.Children.Clear();
        var candidates = new StackPanel { Orientation = view.Layout == "horizontal" ? Orientation.Horizontal : Orientation.Vertical };
        for (int index = 0; index < view.Items.Count; ++index)
        {
            int candidateIndex = index;
            var row = new Border
            {
                Background = index == view.SelectedIndex ? new SolidColorBrush(Color.FromRgb(44, 89, 122)) : Brushes.Transparent,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 1, view.Layout == "horizontal" ? 6 : 0, 1),
                Padding = new Thickness(6, 3, 6, 3),
                Child = new TextBlock
                {
                    Foreground = Brushes.White,
                    Text = $"{index + 1}  {view.Items[index]}",
                },
            };
            row.MouseLeftButtonUp += (_, _) => _ = sendAction(new OverlayMessage { Type = "selectCandidate", ClientId = clientId, StateId = stateId, CandidateIndex = candidateIndex });
            candidates.Children.Add(row);
        }
        _content.Children.Add(candidates);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
        footer.Children.Add(CreateFooterAction("<", "previousPage", clientId, stateId, sendAction));
        footer.Children.Add(new TextBlock { Foreground = Brushes.LightGray, Margin = new Thickness(8, 3, 8, 3), Text = $"{view.Page + 1}/{view.PageCount}" });
        footer.Children.Add(CreateFooterAction(">", "nextPage", clientId, stateId, sendAction));
        if (!string.IsNullOrWhiteSpace(view.ModeMarker)) footer.Children.Add(new TextBlock { Foreground = Brushes.LightSkyBlue, Margin = new Thickness(8, 3, 0, 3), Text = view.ModeMarker });
        _content.Children.Add(footer);

        Left = view.X;
        Top = view.Y;
        if (!IsVisible) Show();
    }

    private static Border CreateFooterAction(string label, string type, string clientId, long stateId, Func<OverlayMessage, Task> sendAction)
    {
        var action = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(48, 58, 68)),
            Cursor = Cursors.Hand,
            Padding = new Thickness(6, 3, 6, 3),
            Child = new TextBlock { Foreground = Brushes.White, Text = label },
        };
        action.MouseLeftButtonUp += (_, _) => _ = sendAction(new OverlayMessage { Type = type, ClientId = clientId, StateId = stateId });
        return action;
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
