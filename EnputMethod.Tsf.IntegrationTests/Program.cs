using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;

namespace EnputMethod.Tsf.IntegrationTests;

internal static class Program
{
    private static readonly Guid TextServiceClsid = new("9C8945D5-01DF-48F4-A8DB-57E8B6A1EB10");
    private static readonly Guid ProfileGuid = new("55F31085-E7CD-4886-BB80-1D61CE392107");
    private const uint TfProfileTypeInputProcessor = 0x0001;
    private const uint TfIppmForProcess = 0x10000000;
    private const uint TfIppmDontCareCurrentInputLanguage = 0x00000004;
    private const ushort ChineseSimplified = 0x0804;
    private static TextBox _editor = null!;

    [STAThread]
    private static int Main()
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Window window = new()
        {
            Title = "Enput TSF Integration Test Host",
            Width = 720,
            Height = 180,
            Content = _editor = new TextBox { FontSize = 24, Margin = new Thickness(20), AcceptsReturn = true },
        };
        try
        {
            window.Show();
            FocusEditor(window);
            ActivateEnputForThisProcess();
            Pump(600);

            VerifyCommittedCandidate("Chicago", "Chicago");
            VerifyCommittedCandidate("Manhattan", "Manhattan");
            VerifyCommittedCandidate("polish", "polish");
            VerifyCommittedCandidate("Polish", "Polish");
            VerifyCommittedCandidate("DonaldT", "Donald Trump");
            VerifyCommittedCandidate("TaylorS", "Taylor Swift");
            VerifyCommittedCandidate("MichaelJ", "Michael Jackson");
            VerifyCommittedCandidate("thewhitehouse", "The White House");
            VerifySymbolComposition("AT&T");
            VerifySymbolComposition("R&B");
            VerifyPreviewCursorPromotion();

            Console.WriteLine("Installed Enput TSF integration tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            window.Close();
            application.Shutdown();
        }
    }

    private static void ActivateEnputForThisProcess()
    {
        Type managerType = Type.GetTypeFromCLSID(new Guid("33C53A50-F456-4884-B049-85FD643ECFED"), throwOnError: true)!;
        var manager = (ITfInputProcessorProfileMgr)Activator.CreateInstance(managerType)!;
        Guid clsid = TextServiceClsid;
        Guid profile = ProfileGuid;
        int hr = manager.ActivateProfile(TfProfileTypeInputProcessor, ChineseSimplified, ref clsid, ref profile,
            IntPtr.Zero, TfIppmForProcess | TfIppmDontCareCurrentInputLanguage);
        Marshal.ThrowExceptionForHR(hr);
    }

    private static void VerifyCommittedCandidate(string query, string expected)
    {
        ResetEditor();
        TypeText(query);
        Pump(500);
        PressKey(0x0D);
        WaitFor(() => string.Equals(_editor.Text.TrimEnd(), expected, StringComparison.Ordinal), $"{query} did not commit {expected}.");
    }

    private static void VerifySymbolComposition(string query)
    {
        ResetEditor();
        TypeText(query);
        WaitFor(() => _editor.Text == query, $"{query} did not remain a single active composition after &.");
        PressKey(0x72); // F3
        Pump(500);
        PressKey(0x0D);
        WaitFor(() => string.Equals(_editor.Text.TrimEnd(), query, StringComparison.Ordinal), $"{query} did not commit after F3.");
    }

    private static void VerifyPreviewCursorPromotion()
    {
        ResetEditor();
        TypeText("Wash");
        Pump(500);
        PressKey(0x28); // Down selects Washington DC after Washington.
        Pump(250);
        PressKey(0x25); // Left promotes the preview, then moves from C to before C.
        WaitFor(() => _editor.Text == "Washington DC" && _editor.CaretIndex == "Washington D".Length,
            "Wash preview did not become Washington D|C after Down then Left.");
        PressKey(0x0D);
        WaitFor(() => string.Equals(_editor.Text.TrimEnd(), "Washington DC", StringComparison.Ordinal), "Washington DC did not commit after preview editing.");
    }

    private static void ResetEditor()
    {
        PressKey(0x1B); // Escape ends any prior composition before changing the test host text.
        Pump(100);
        _editor.Text = string.Empty;
        _editor.CaretIndex = 0;
        FocusEditor(null);
        Pump(100);
    }

    private static void TypeText(string text)
    {
        foreach (char character in text) SendCharacter(character);
    }

    private static void SendCharacter(char character)
    {
        short mapped = VkKeyScan(character);
        if (mapped == -1) throw new InvalidOperationException($"No virtual-key mapping exists for {character}.");
        byte virtualKey = (byte)mapped;
        byte modifiers = (byte)(mapped >> 8);
        if ((modifiers & 1) != 0) KeyEvent(0x10, false);
        if ((modifiers & 2) != 0) KeyEvent(0x11, false);
        if ((modifiers & 4) != 0) KeyEvent(0x12, false);
        KeyEvent(virtualKey, false);
        KeyEvent(virtualKey, true);
        if ((modifiers & 4) != 0) KeyEvent(0x12, true);
        if ((modifiers & 2) != 0) KeyEvent(0x11, true);
        if ((modifiers & 1) != 0) KeyEvent(0x10, true);
        Pump(35);
    }

    private static void PressKey(ushort virtualKey)
    {
        KeyEvent(virtualKey, false);
        KeyEvent(virtualKey, true);
        Pump(80);
    }

    private static void KeyEvent(ushort virtualKey, bool keyUp)
    {
        INPUT input = new()
        {
            type = 1,
            Union = new InputUnion { Keyboard = new KEYBDINPUT { VirtualKey = virtualKey, Flags = keyUp ? 0x0002u : 0u } },
        };
        if (SendInput(1, [input], Marshal.SizeOf<INPUT>()) != 1) throw new InvalidOperationException($"SendInput failed for virtual key {virtualKey}.");
    }

    private static void FocusEditor(Window? window)
    {
        if (window is not null)
        {
            window.Activate();
            SetForegroundWindow(new WindowInteropHelper(window).Handle);
        }
        _editor.Focus();
        Keyboard.Focus(_editor);
        Pump(80);
    }

    private static void WaitFor(Func<bool> condition, string failure)
    {
        for (int attempt = 0; attempt < 50; ++attempt)
        {
            if (condition()) return;
            Pump(100);
        }
        throw new InvalidOperationException(failure + $" Text=[{_editor.Text}] Caret={_editor.CaretIndex}.");
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [ComImport, Guid("71C6E74C-0F28-11D8-A82A-00065B84435C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfInputProcessorProfileMgr
    {
        [PreserveSig]
        int ActivateProfile(uint profileType, ushort language, ref Guid clsid, ref Guid profile, IntPtr keyboardLayout, uint flags);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion Union; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort VirtualKey; public ushort ScanCode; public uint Flags; public uint Time; public IntPtr ExtraInfo; }

    // INPUT is 40 bytes on x64 because the union also contains MOUSEINPUT.
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public IntPtr ExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char character);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
