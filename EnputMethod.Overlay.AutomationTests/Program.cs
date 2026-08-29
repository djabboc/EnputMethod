using EnputMethod.Overlay;
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
