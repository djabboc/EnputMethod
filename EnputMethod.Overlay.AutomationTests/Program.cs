using EnputMethod.Overlay;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;

namespace EnputMethod.Overlay.AutomationTests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            VerifyForegroundOwnerArbitration();
            VerifyOwnerWindowProtocol();
            VerifyEmojiAssetNaming();
            VerifyEmojiCandidateUsesColorAsset();
            VerifyCandidateWindowWarmup();
            VerifyPointFontSizeUsesWpfDips();
            VerifyPaginationLayoutAndHover();
            VerifyTranslationWindowUsesRichTextAndConfiguredSize();
            VerifyTranslationWindowKeepsNativeSelectionSurface();
            if (args.Contains("--real-input", StringComparer.OrdinalIgnoreCase))
                VerifyRealNoActivateMouseSelectionAndCopy();
            Console.WriteLine("Overlay foreground automation tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifyForegroundOwnerArbitration()
    {
        using HwndSource firstEditor = CreateEditorWindow("first-editor");
        using HwndSource secondEditor = CreateEditorWindow("second-editor");

        Assert(OverlayFocus.IsOwnerOfForeground(firstEditor.Handle.ToInt64(), firstEditor.Handle), "The foreground editor must own its candidate window.");
        Assert(!OverlayFocus.IsOwnerOfForeground(firstEditor.Handle.ToInt64(), secondEditor.Handle), "A background editor must not own the foreground candidate window.");
        Assert(OverlayFocus.IsOwnerOfForeground(0, secondEditor.Handle), "Legacy messages without an owner window remain compatible.");
    }

    private static void VerifyOwnerWindowProtocol()
    {
        const string message = """{"type":"showCandidates","clientId":"host-1","stateId":4,"candidates":{"x":120,"y":80,"ownerWindow":42,"items":["hello"],"page":0,"pageCount":1,"selectedIndex":0,"layout":"vertical"}}""";
        Assert(OverlayProtocol.TryParse(message, out OverlayMessage? parsed) && parsed?.Candidates?.OwnerWindow == 42, "The candidate owner window must survive protocol parsing.");
    }

    private static void VerifyEmojiAssetNaming()
    {
        Assert(EmojiAssetResolver.FileNameFor("😀") == "1f600.png", "Grinning face must use its Twemoji asset name.");
        Assert(EmojiAssetResolver.FileNameFor("❤️") == "2764.png", "Variation selectors must not appear in Twemoji asset names.");
        Assert(EmojiAssetResolver.FileNameFor("👩‍💻") == "1f469-200d-1f4bb.png", "ZWJ Emoji must preserve all visible code points.");
        Assert(EmojiAssetResolver.EmojiFromCandidate("❤️\u001Fheart, love") == "❤️", "Emoji candidate text must be separated from its keywords.");
        Assert(EmojiAssetResolver.LabelFromCandidate("❤️\u001Fheart, love") == "heart, love", "Emoji candidate keyword labels must remain visible.");
    }
    private static void VerifyEmojiCandidateUsesColorAsset()
    {
        var overlay = new CandidateOverlayWindow();
        try
        {
            overlay.ShowCandidates("emoji-test", 1, new CandidateView
            {
                Items = ["😀\u001Fgrinning, smile"],
                Page = 0,
                PageCount = 1,
                SelectedIndex = 0,
                Layout = "vertical",
                ModeMarker = "EMOJI",
            }, _ => Task.CompletedTask);

            var frame = (Border)overlay.Content;
            var root = (StackPanel)frame.Child;
            var candidates = (StackPanel)root.Children[0];
            var row = (Border)candidates.Children[0];
            var content = (StackPanel)row.Child;
            Assert(content.Children.OfType<Image>().SingleOrDefault() is Image { Source: not null }, "Emoji candidates must render their bundled color image instead of a monochrome font glyph.");
        }
        finally
        {
            overlay.Close();
        }
    }
    private static void VerifyCandidateWindowWarmup()
    {
        var overlay = new CandidateOverlayWindow();
        try
        {
            overlay.WarmUp();
            Assert(new WindowInteropHelper(overlay).Handle != IntPtr.Zero, "Warmup must create the native window before the first candidate arrives.");
            Assert(!overlay.IsVisible, "Warmup must not leave a visible candidate window on screen.");
        }
        finally
        {
            overlay.Close();
        }
    }
    private static void VerifyPointFontSizeUsesWpfDips()
    {
        var overlay = new CandidateOverlayWindow();
        try
        {
            overlay.ShowCandidates("font-test", 1, new CandidateView
            {
                Items = ["hello"],
                Page = 0,
                PageCount = 1,
                SelectedIndex = 0,
                Layout = "vertical",
                Theme = new OverlayTheme { FontSize = 18 },
            }, _ => Task.CompletedTask);

            var frame = (Border)overlay.Content;
            var root = (StackPanel)frame.Child;
            var candidates = (StackPanel)root.Children[0];
            var row = (Border)candidates.Children[0];
            Assert(row.Child is TextBlock { FontSize: 24 }, "An 18pt native font must render as 24 WPF DIPs.");
        }
        finally
        {
            overlay.Close();
        }
    }
    private static void VerifyPaginationLayoutAndHover()
    {
        var overlay = new CandidateOverlayWindow();
        try
        {
            overlay.ShowCandidates("layout-test", 1, new CandidateView
            {
                Items = ["encyclopedia"],
                Page = 0,
                PageCount = 2,
                SelectedIndex = 0,
                Layout = "vertical",
            }, _ => Task.CompletedTask);

            var frame = (Border)overlay.Content;
            var root = (StackPanel)frame.Child;
            var footer = (Grid)root.Children[1];
            Assert(footer.HorizontalAlignment == HorizontalAlignment.Stretch && double.IsNaN(footer.Width), "The pager must span the candidate width instead of using a fixed control group.");
            Assert(footer.ColumnDefinitions.Count == 4, "The pager must reserve opposing edge columns for both navigation buttons.");
            var previous = (Border)footer.Children[0];
            var next = (Border)footer.Children[^1];
            Assert(Grid.GetColumn(previous) == 0 && Grid.GetColumn(next) == 3, "Previous and next controls must stay at opposite candidate-frame edges.");
            next.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            Assert(next.Background is SolidColorBrush { Color: var color } && color == (Color)ColorConverter.ConvertFromString("#2c597a"), "An available navigation button must highlight on hover.");
        }
        finally
        {
            overlay.Close();
        }
    }
    private static void VerifyTranslationWindowUsesRichTextAndConfiguredSize()
    {
        var overlay = new TranslationOverlayWindow();
        try
        {
            overlay.ShowTranslation("translation-test", new TranslationView
            {
                Title = "braces",
                Content = "noun\nen: n. a dental appliance\nzh-CN: 牙套\nExample: She wears braces.",
                Theme = new OverlayTheme { FontSize = 18, TranslationWindowWidth = 460, TranslationWindowHeight = 340 },
            }, null);
            Assert(overlay.Width == 460 && overlay.Height == 340, "Translation window must use the configured persistent dimensions.");
            var frame = (Border)overlay.Content;
            var panel = (Grid)frame.Child;
            var content = (RichTextBox)panel.Children[1];
            Assert(content.Document.Blocks.Count == 4, "Translation content must be rendered as structured FlowDocument paragraphs.");
            Assert(content.Document.Blocks.OfType<Paragraph>().Any(paragraph => paragraph.Inlines.OfType<Run>().Any(run => run.Text == "zh-CN: ")), "Language labels must remain semantic rich-text runs.");
            Assert(content.IsReadOnly && content.IsDocumentEnabled && content.IsHitTestVisible && content.IsInactiveSelectionHighlightEnabled && content.ContextMenu is not null, "Translation rich text must remain visibly mouse-selectable without activating the overlay and expose a copy command.");
            Assert(content.Resources[typeof(ScrollBar)] is Style, "Translation scrollbar styling must come from the supplied theme.");
            content.SelectAll();
            Assert(content.Selection.Text.Contains("牙套", StringComparison.Ordinal), "Translation selection must include rendered rich text.");
            overlay.Width = 520;
            overlay.Height = 360;
            overlay.ShowTranslation("translation-test", new TranslationView
            {
                Title = "braces",
                Content = "en: n. a dental appliance",
                Theme = new OverlayTheme { TranslationWindowWidth = 460, TranslationWindowHeight = 340 },
            }, null);
            Assert(overlay.Width == 520 && overlay.Height == 360, "A user-resized translation window must not be reset by a stale service message.");
        }
        finally
        {
            overlay.Close();
        }
    }
    private static void VerifyTranslationWindowKeepsNativeSelectionSurface()
    {
        var overlay = new TranslationOverlayWindow();
        try
        {
            overlay.ShowTranslation("selection-test", new TranslationView
            {
                Title = "selection",
                Content = "selectable translation text",
            }, null);
            var frame = (Border)overlay.Content;
            var panel = (Grid)frame.Child;
            var content = (RichTextBox)panel.Children[1];
            Assert(!overlay.Focusable, "The translation window must remain non-activating so the TSF editor host keeps focus.");
            Assert(content.IsReadOnly && content.IsHitTestVisible && content.Focusable && content.IsInactiveSelectionHighlightEnabled, "The translation surface must leave native RichTextBox selection enabled.");
            content.SelectAll();
            Assert(content.Selection.Text.Contains("selectable translation text", StringComparison.Ordinal), "Native RichTextBox selection must include the rendered translation text.");
            Assert(content.ContextMenu?.Items.OfType<MenuItem>().SingleOrDefault() is not null, "A selected translation must retain the Copy context menu.");
        }
        finally
        {
            overlay.Close();
        }
    }

    private static void VerifyRealNoActivateMouseSelectionAndCopy()
    {
        Assert(Environment.UserInteractive, "The real overlay input test requires an interactive Windows desktop.");
        var overlay = new TranslationOverlayWindow();
        IDataObject? originalClipboard = null;
        bool clipboardCaptured = false;
        NativePoint originalPointer = default;
        bool pointerCaptured = GetCursorPos(out originalPointer);
        try
        {
            originalClipboard = Clipboard.GetDataObject();
            clipboardCaptured = true;
            Rect workArea = SystemParameters.WorkArea;
            overlay.ShowTranslation("real-input-test", new TranslationView
            {
                Title = "real input test",
                Content = "selectable translation text",
                CandidateRight = (int)Math.Round(workArea.Right - 420),
                CandidateTop = (int)Math.Round(workArea.Bottom - 340),
            }, null);
            overlay.UpdateLayout();
            PumpDispatcher(TimeSpan.FromMilliseconds(150));

            var frame = (Border)overlay.Content;
            var panel = (Grid)frame.Child;
            var content = (RichTextBox)panel.Children[1];
            content.UpdateLayout();
            Run run = ((Paragraph)content.Document.Blocks.FirstBlock!).Inlines.OfType<Run>().Single();
            Rect startBounds = run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            Rect endBounds = run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            Assert(!startBounds.IsEmpty && !endBounds.IsEmpty, "The real-input test must render selectable text before injecting mouse input.");

            Point start = content.PointToScreen(new Point(startBounds.Left + Math.Min(2, Math.Max(1, startBounds.Width / 2)), startBounds.Top + Math.Max(1, startBounds.Height / 2)));
            Point end = content.PointToScreen(new Point(endBounds.Right - Math.Min(2, Math.Max(1, endBounds.Width / 2)), endBounds.Top + Math.Max(1, endBounds.Height / 2)));
            content.Selection.Select(content.Document.ContentStart, content.Document.ContentStart);

            RunInjectedInput(() =>
            {
                SendMouseMove(start);
                Thread.Sleep(40);
                SendMouseButton(MouseEventLeftDown);
                for (int step = 1; step <= 6; step++)
                {
                    Thread.Sleep(30);
                    double progress = step / 6.0;
                    SendMouseMove(new Point(start.X + ((end.X - start.X) * progress), start.Y + ((end.Y - start.Y) * progress)));
                }
                Thread.Sleep(30);
                SendMouseButton(MouseEventLeftUp);
            }, TimeSpan.FromSeconds(1));

            string selected = content.Selection.Text;
            Assert(selected.Contains("translation", StringComparison.Ordinal), "A real SendInput drag must select translation text in the no-activate RichTextBox.");

            RunInjectedInput(() =>
            {
                SendMouseMove(end);
                Thread.Sleep(30);
                SendMouseButton(MouseEventRightDown);
                Thread.Sleep(30);
                SendMouseButton(MouseEventRightUp);
            }, TimeSpan.FromSeconds(1));

            ContextMenu menu = content.ContextMenu ?? throw new InvalidOperationException("Translation content must expose a context menu.");
            Assert(menu.IsOpen, "A real right click on selected translation text must open the Copy context menu.");
            MenuItem copy = menu.Items.OfType<MenuItem>().Single();
            Assert(copy.IsEnabled, "Copy must be enabled after a real mouse selection.");
            copy.UpdateLayout();
            Assert(copy.ActualWidth > 0 && copy.ActualHeight > 0, "The Copy context-menu item must be laid out before receiving a real click.");
            Point copyCenter = copy.PointToScreen(new Point(copy.ActualWidth / 2, copy.ActualHeight / 2));
            RunInjectedInput(() =>
            {
                SendMouseMove(copyCenter);
                Thread.Sleep(30);
                SendMouseButton(MouseEventLeftDown);
                Thread.Sleep(30);
                SendMouseButton(MouseEventLeftUp);
            }, TimeSpan.FromSeconds(1));
            Assert(Clipboard.ContainsText() && Clipboard.GetText().Contains("translation", StringComparison.Ordinal), "Copy must place the real mouse selection on the clipboard.");
            Console.WriteLine("Real native mouse selection and copy test passed.");
        }
        finally
        {
            if (overlay.IsVisible) overlay.Hide();
            overlay.Close();
            if (clipboardCaptured)
            {
                try
                {
                    if (originalClipboard is null) Clipboard.Clear();
                    else Clipboard.SetDataObject(originalClipboard, true);
                }
                catch (ExternalException)
                {
                    // The test never leaves the replacement data intentionally; another process may hold the clipboard.
                }
            }
            if (pointerCaptured) SetCursorPos(originalPointer.X, originalPointer.Y);
        }
    }

    private static void SendMouseMove(Point point)
    {
        (int x, int y) = NormalizeAbsoluteMouse(point);
        SendMouseInput(MouseEventMove | MouseEventAbsolute | MouseEventVirtualDesk, x, y);
    }

    private static void SendMouseButton(uint buttonFlag)
    {
        SendMouseInput(buttonFlag, 0, 0);
    }

    private static void SendMouseInput(uint flags, int x, int y)
    {
        var input = new NativeInput
        {
            Type = InputMouse,
            Union = new NativeInputUnion
            {
                Mouse = new NativeMouseInput
                {
                    Dx = x,
                    Dy = y,
                    Flags = flags,
                },
            },
        };
        uint sent = SendInput(1, [input], Marshal.SizeOf<NativeInput>());
        Assert(sent == 1, $"SendInput failed with Win32 error {Marshal.GetLastWin32Error()}.");
    }

    private static (int X, int Y) NormalizeAbsoluteMouse(Point point)
    {
        int left = GetSystemMetrics(SystemMetricXVirtualScreen);
        int top = GetSystemMetrics(SystemMetricYVirtualScreen);
        int width = Math.Max(1, GetSystemMetrics(SystemMetricCxVirtualScreen));
        int height = Math.Max(1, GetSystemMetrics(SystemMetricCyVirtualScreen));
        int x = (int)Math.Round(point.X);
        int y = (int)Math.Round(point.Y);
        return (
            (int)Math.Clamp(((long)(x - left) * 65535) / Math.Max(1, width - 1), 0, 65535),
            (int)Math.Clamp(((long)(y - top) * 65535) / Math.Max(1, height - 1), 0, 65535));
    }

    private static void RunInjectedInput(Action action, TimeSpan timeout)
    {
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        })
        {
            IsBackground = true,
        };
        worker.Start();
        PumpDispatcher(timeout);
        if (!worker.Join(250)) throw new InvalidOperationException("The real input worker did not complete within its timeout.");
        if (failure is not null) throw new InvalidOperationException("The real input worker failed.", failure);
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = duration,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private const uint InputMouse = 0;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventVirtualDesk = 0x4000;
    private const uint MouseEventAbsolute = 0x8000;
    private const int SystemMetricXVirtualScreen = 76;
    private const int SystemMetricYVirtualScreen = 77;
    private const int SystemMetricCxVirtualScreen = 78;
    private const int SystemMetricCyVirtualScreen = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        internal uint Type;
        internal NativeInputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)]
        internal NativeMouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        internal int Dx;
        internal int Dy;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, [In] NativeInput[] inputs, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private static HwndSource CreateEditorWindow(string name)
    {
        return new HwndSource(new HwndSourceParameters(name)
        {
            PositionX = -32000,
            PositionY = -32000,
            Width = 1,
            Height = 1,
            WindowStyle = 0,
        });
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
