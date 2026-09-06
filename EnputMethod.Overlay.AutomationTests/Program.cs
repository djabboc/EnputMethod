using EnputMethod.Overlay;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
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
            if (args.Length == 1) VerifyFailedListenerAllowsHealthyTakeover(args[0]);
            VerifyForegroundOwnerArbitration();
            VerifyOwnerWindowProtocol();
            VerifyEmojiAssetNaming();
            VerifyEmojiCandidateUsesColorAsset();
            VerifyCandidateWindowWarmup();
            VerifyPointFontSizeUsesWpfDips();
            VerifyPaginationLayoutAndHover();
            VerifyCandidatePositioningAvoidsComposition();
            VerifyTranslationWindowUsesRichTextAndConfiguredSize();
            VerifyTranslationCopyFeedback();
            Console.WriteLine("Overlay foreground automation tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifyFailedListenerAllowsHealthyTakeover(string overlayExecutable)
    {
        Assert(File.Exists(overlayExecutable), $"Overlay lifecycle executable does not exist: {overlayExecutable}");
        string token = Guid.NewGuid().ToString("N");
        string mutexName = $"Local\\EnputMethod.Overlay.LifecycleTest.{token}";
        string pipeName = $"EnputMethod.Overlay.LifecycleTest.{token}";

        using (Process failed = StartOverlay(overlayExecutable, $"--lifecycle-test={token}", "--fail-pipe-listener"))
        {
            if (!failed.WaitForExit(5000))
            {
                failed.Kill(entireProcessTree: true);
                throw new InvalidOperationException("An Overlay with a failed listener did not exit promptly.");
            }
            Assert(failed.ExitCode == 1, $"A failed Overlay listener must exit with code 1, got {failed.ExitCode}.");
        }

        using (var takeoverMutex = new Mutex(true, mutexName, out bool createdNew))
        {
            Assert(createdNew, "The failed Overlay process retained its single-instance mutex.");
            takeoverMutex.ReleaseMutex();
        }

        using Process healthy = StartOverlay(overlayExecutable, $"--lifecycle-test={token}");
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(5000);
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
            string? ready = reader.ReadLine();
            Assert(ready is not null && ready.Contains("\"ready\"", StringComparison.Ordinal), "A healthy Overlay could not take over after the failed instance.");
            Console.WriteLine("Overlay failed-listener exit and healthy takeover test passed.");
        }
        finally
        {
            if (!healthy.HasExited)
            {
                healthy.Kill(entireProcessTree: true);
                healthy.WaitForExit(5000);
            }
        }
    }

    private static Process StartOverlay(string executable, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        return Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start Overlay lifecycle process: {executable}");
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
        const string message = """{"type":"showCandidates","clientId":"host-1","stateId":4,"candidates":{"x":120,"y":80,"compositionLeft":120,"compositionTop":56,"compositionRight":168,"compositionBottom":80,"ownerWindow":42,"items":[{"text":"hello"}],"page":0,"pageCount":1,"selectedIndex":0,"layout":"vertical"}}""";
        Assert(OverlayProtocol.TryParse(message, out OverlayMessage? parsed) && parsed?.Candidates?.OwnerWindow == 42, "The candidate owner window must survive protocol parsing.");
        Assert(parsed?.Candidates?.HasCompositionBounds == true, "Candidate messages must carry the complete composition bounds for collision avoidance.");
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
                Items = [new CandidateItemView { Text = "😀\u001Fgrinning, smile" }],
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
            Assert(overlay.Title == "Enput Candidate Overlay", "Candidate windows need a stable native title for real-host automation.");
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
                Items = [new CandidateItemView { Text = "hello" }],
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
                Items = [new CandidateItemView { Text = "encyclopedia" }],
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
    private static void VerifyCandidatePositioningAvoidsComposition()
    {
        Rect workArea = new(0, 0, 1920, 1080);
        Size candidateSize = new(280, 260);
        Rect bottomComposition = new(640, 1040, 80, 24);
        Point bottom = OverlayPositioning.PlaceCandidate(bottomComposition, candidateSize, workArea);
        Assert(bottom.Y + candidateSize.Height <= bottomComposition.Top - 2, "Bottom-edge candidates must move above the composition instead of covering it.");

        Rect topComposition = new(32, 16, 80, 24);
        Point top = OverlayPositioning.PlaceCandidate(topComposition, candidateSize, workArea);
        Assert(top.Y >= topComposition.Bottom + 2, "Top-edge candidates must remain below the composition.");

        Rect rightComposition = new(1880, 900, 30, 24);
        Point right = OverlayPositioning.PlaceCandidate(rightComposition, candidateSize, workArea);
        Assert(right.X + candidateSize.Width <= workArea.Right, "Right-edge candidates must remain inside the work area.");
    }

    private static void VerifyTranslationWindowUsesRichTextAndConfiguredSize()
    {
        const string translationText = "noun\nen: n. a dental appliance\nzh-CN: 牙套\nExample: She wears braces.";
        string? copiedText = null;
        var overlay = new TranslationOverlayWindow(text => copiedText = text);
        try
        {
            Assert(overlay.Title == "Enput Translation Overlay", "Translation windows need a stable native title for real-host automation.");
            overlay.ShowTranslation("translation-test", new TranslationView
            {
                Title = "braces",
                Content = translationText,
                Theme = new OverlayTheme { FontSize = 18, TranslationWindowWidth = 460, TranslationWindowHeight = 340 },
            }, null);
            Assert(overlay.Width == 460 && overlay.Height == 340, "Translation window must use the configured persistent dimensions.");
            var frame = (Border)overlay.Content;
            var panel = (Grid)frame.Child;
            var header = (Grid)panel.Children[0];
            var content = (RichTextBox)panel.Children[1];
            Assert(content.Document.Blocks.Count == 4, "Translation content must be rendered as structured FlowDocument paragraphs.");
            Assert(content.Document.Blocks.OfType<Paragraph>().Any(paragraph => paragraph.Inlines.OfType<Run>().Any(run => run.Text == "zh-CN: ")), "Language labels must remain semantic rich-text runs.");
            Assert(header.ColumnDefinitions.Count == 2 && header.Children[1] is Border { Child: TextBlock { Text: "Copy" } }, "Translation title bars must expose a visible Copy action.");
            Assert(content.IsReadOnly && content.IsDocumentEnabled && content.IsHitTestVisible && !content.Focusable && content.ContextMenu is null, "Translation content must remain a non-focusable rich-text view without a selection copy route.");
            Assert(content.Resources[typeof(ScrollBar)] is Style, "Translation scrollbar styling must come from the supplied theme.");
            var copyAction = (Border)header.Children[1];
            copyAction.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
            Assert(copiedText == translationText, "The Copy action must copy the complete translation text without changing focus.");
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
    private static void VerifyTranslationCopyFeedback()
    {
        string copied = string.Empty;
        var overlay = new TranslationOverlayWindow(text => copied = text);
        try
        {
            overlay.ShowTranslation("copy-test", new TranslationView { Title = "hello", Content = "en: greeting" }, null);
            overlay.CopyTranslation();
            Assert(copied == "en: greeting", "Copy must write the full translation text to the clipboard target.");
            Assert(overlay.IsCopyFeedbackVisible && overlay.CopyButtonText == "✓ Copied", "A successful copy must show a visible confirmed state.");

            var frame = new DispatcherFrame();
            var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1750) };
            wait.Tick += (_, _) =>
            {
                wait.Stop();
                frame.Continue = false;
            };
            wait.Start();
            Dispatcher.PushFrame(frame);
            Assert(!overlay.IsCopyFeedbackVisible && overlay.CopyButtonText == "Copy", "The copy confirmation must return to the normal button state after its timeout.");
        }
        finally
        {
            overlay.Close();
        }
    }

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
