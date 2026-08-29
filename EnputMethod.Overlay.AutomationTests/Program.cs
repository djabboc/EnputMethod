using EnputMethod.Overlay;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;

namespace EnputMethod.Overlay.AutomationTests;

internal static class Program
{
    [STAThread]
    private static int Main()
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
            Assert(content.IsReadOnly && content.IsDocumentEnabled && content.IsHitTestVisible && content.ContextMenu is not null, "Translation rich text must remain mouse-selectable and expose a copy command.");
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
