using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
    private const string CandidateOverlayTitle = "Enput Candidate Overlay";
    private const string TranslationOverlayTitle = "Enput Translation Overlay";
    private const string CandidateFrequencyRegistryPath = @"Software\Enput Method\CandidateFrequency";
    private const string DisableCandidateFrequencyPersistenceEnvironment = "ENPUT_TEST_DISABLE_CANDIDATE_FREQUENCY_PERSISTENCE";
    private static TextBox _editor = null!;
    private static Window _window = null!;
    private static IntPtr _windowHandle;
    private static LogCheckpoint _tsfDiagnostics;
    private static LogCheckpoint _overlayDiagnostics;
    private static string _overlayClientId = string.Empty;

    [STAThread]
    private static int Main()
    {
        try
        {
            RunIntegrationTests();
            Console.WriteLine("Installed Enput TSF integration tests passed, including visible Overlay windows.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return exception is TestInterferenceException ? 2 : 1;
        }
    }

    private static void RunIntegrationTests()
    {
        string frequencySnapshot = CaptureCandidateFrequencyRegistry();
        string? priorPersistenceSetting = Environment.GetEnvironmentVariable(DisableCandidateFrequencyPersistenceEnvironment);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _tsfDiagnostics = CaptureLogCheckpoint(Path.Combine(localAppData, "Enput Method", "overlay-diagnostics.log"));
        _overlayDiagnostics = CaptureLogCheckpoint(Path.Combine(localAppData, "Enput Method", "UserData", "overlay-diagnostics.log"));
        Environment.SetEnvironmentVariable(DisableCandidateFrequencyPersistenceEnvironment, "1");
        Application? application = null;
        Exception? failure = null;
        try
        {
            application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            _window = new Window
            {
                Title = "Enput TSF Integration Test Host",
                Width = 720,
                Height = 180,
                Content = _editor = new TextBox { FontSize = 24, Margin = new Thickness(20), AcceptsReturn = true, AcceptsTab = true },
            };
            ActivateEnputForThisProcess();
            _window.Show();
            _windowHandle = new WindowInteropHelper(_window).Handle;
            FocusEditor();
            Pump(600);
            _overlayClientId = WaitForOverlayReady();

            VerifyCommittedCandidate("Chicago", "Chicago");
            VerifyCommittedCandidate("Manhattan", "Manhattan");
            VerifyCommittedCandidate("polish", "polish");
            VerifyCommittedCandidate("Polish", "Polish");
            VerifyCasePriorityAfterExact("polis", "polish");
            VerifyCasePriorityAfterExact("Polis", "Polish");
            VerifyAdjacentCaseVariant("polish", "Polish");
            VerifyAdjacentCaseVariant("Polish", "polish");
            VerifyCommittedCandidate("DonaldT", "Donald Trump");
            VerifyCommittedCandidate("TaylorS", "Taylor Swift");
            VerifyCommittedCandidate("MichaelJ", "Michael Jackson");
            VerifyCommittedCandidate("thewhitehouse", "The White House");
            VerifySymbolComposition("AT&T", translationAlreadyEnabled: false);
            VerifySymbolComposition("R&B", translationAlreadyEnabled: true);
            VerifyPreviewCursorPromotion();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                if (_windowHandle != IntPtr.Zero) _window.Close();
                application?.Shutdown();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
            Environment.SetEnvironmentVariable(DisableCandidateFrequencyPersistenceEnvironment, priorPersistenceSetting);
        }

        try
        {
            if (failure is null) VerifyOverlayDiagnosticEvidence();
            string finalFrequencySnapshot = CaptureCandidateFrequencyRegistry();
            if (!string.Equals(frequencySnapshot, finalFrequencySnapshot, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The integration test changed HKCU\\Software\\Enput Method\\CandidateFrequency.");
            }
        }
        catch (Exception exception)
        {
            failure = failure is null ? exception : new AggregateException(failure, exception);
        }

        if (failure is not null) throw failure;
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

    private static void VerifyCommittedCandidate(string query, string expected, ushort selectionKey = (ushort)'1')
    {
        ResetEditor();
        TypeText(query);
        WaitForOverlayWindow(CandidateOverlayTitle, $"{query} did not show the real candidate Overlay window.");
        PressKey(selectionKey); // WPF does not route Tab through this TSF host; use Enput's built-in candidate selection.
        WaitFor(() => string.Equals(_editor.Text.TrimEnd(), expected, StringComparison.Ordinal), $"{query} did not commit {expected}.");
    }

    private static void VerifyCasePriorityAfterExact(string query, string expected)
    {
        ResetEditor();
        TypeText(query);
        WaitForOverlayWindow(CandidateOverlayTitle, $"{query} did not show the real candidate Overlay window.");
        PressKey(0x28); // The exact dictionary word "polis" remains first; typed case weakly orders the following variants.
        WaitFor(() => _editor.Text == expected, $"{query} did not prioritize {expected} immediately after its exact match.");
        PressKey(0x0D);
        WaitFor(() => string.Equals(_editor.Text.TrimEnd(), expected, StringComparison.Ordinal), $"{query} did not commit the prioritized {expected} candidate.");
    }

    private static void VerifyAdjacentCaseVariant(string query, string expectedNext)
    {
        ResetEditor();
        TypeText(query);
        WaitForOverlayWindow(CandidateOverlayTitle, $"{query} did not show the real candidate Overlay window.");
        PressKey(0x28);
        WaitFor(() => _editor.Text == expectedNext, $"{query} did not expose {expectedNext} as its adjacent distinct dictionary entry.");
        PressKey(0x0D);
        WaitFor(() => string.Equals(_editor.Text.TrimEnd(), expectedNext, StringComparison.Ordinal), $"{query} did not commit adjacent entry {expectedNext}.");
    }

    private static void VerifySymbolComposition(string query, bool translationAlreadyEnabled)
    {
        ResetEditor();
        TypeText(query);
        WaitFor(() => _editor.Text == query, $"{query} did not remain a single active composition after &.");
        WaitForOverlayWindow(CandidateOverlayTitle, $"{query} did not show the real candidate Overlay window.");
        if (translationAlreadyEnabled)
        {
            WaitForOverlayWindow(TranslationOverlayTitle, $"{query} did not retain the enabled translation Overlay state.");
            PressKey(0x72); // Confirm that F3 can still hide and re-open this query's translation.
            WaitFor(() => !IsOverlayWindowVisible(TranslationOverlayTitle), $"{query} did not hide the translation Overlay after F3.");
        }
        PressKey(0x72); // F3
        WaitForOverlayWindow(TranslationOverlayTitle, $"{query} did not show the real translation Overlay window after F3.");
        PressKey(0x0D);
        WaitFor(() => string.Equals(_editor.Text.TrimEnd(), query, StringComparison.Ordinal), $"{query} did not commit after F3.");
    }

    private static void VerifyPreviewCursorPromotion()
    {
        ResetEditor();
        TypeText("Wash");
        WaitForOverlayWindow(CandidateOverlayTitle, "Wash did not show the real candidate Overlay window.");
        for (int attempt = 0; attempt < 32 && _editor.Text != "Washington DC"; ++attempt) PressKey(0x28);
        if (_editor.Text != "Washington DC") throw new InvalidOperationException("Wash did not expose Washington DC for preview editing.");
        PressKey(0x25); // Left promotes the preview, then moves from C to before C.
        WaitFor(() => _editor.Text == "Washington DC" && _editor.CaretIndex == "Washington D".Length,
            "Wash preview did not become Washington D|C after Down then Left.");
        PressKey(0x0D);
        WaitFor(() => string.Equals(_editor.Text.TrimEnd(), "Washington DC", StringComparison.Ordinal), "Washington DC did not commit after preview editing.");
    }

    private static void ResetEditor()
    {
        FocusEditor();
        PressKey(0x1B); // Escape ends any prior composition before changing the test host text.
        WaitFor(() => !IsOverlayWindowVisible(CandidateOverlayTitle) && !IsOverlayWindowVisible(TranslationOverlayTitle),
            "A prior Overlay window remained visible after composition reset.");
        _editor.Text = string.Empty;
        _editor.CaretIndex = 0;
        FocusEditor();
        Pump(100);
    }

    private static void TypeText(string text)
    {
        foreach (char character in text) SendCharacter(character);
    }

    private static void SendCharacter(char character)
    {
        EnsureEditorForeground();
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
        EnsureEditorForeground();
    }

    private static void PressKey(ushort virtualKey)
    {
        EnsureEditorForeground();
        KeyEvent(virtualKey, false);
        KeyEvent(virtualKey, true);
        Pump(80);
        EnsureEditorForeground();
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

    private static void FocusEditor()
    {
        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint currentThread = GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            _window.Topmost = true;
            _window.Activate();
            BringWindowToTop(_windowHandle);
            SetForegroundWindow(_windowHandle);
            SetActiveWindow(_windowHandle);
        }
        finally
        {
            if (attached) AttachThreadInput(currentThread, foregroundThread, false);
        }
        _editor.Focus();
        Keyboard.Focus(_editor);
        _window.Topmost = false;
        Pump(120);
        EnsureEditorForeground();
    }

    private static void EnsureEditorForeground()
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground != _windowHandle || !_editor.IsKeyboardFocused)
        {
            throw new TestInterferenceException($"The integration-test host lost keyboard focus. Foreground=0x{foreground.ToInt64():X}, Host=0x{_windowHandle.ToInt64():X}.");
        }
    }

    private static void WaitFor(Func<bool> condition, string failure)
    {
        for (int attempt = 0; attempt < 50; ++attempt)
        {
            EnsureEditorForeground();
            if (condition()) return;
            Pump(100);
        }
        throw new InvalidOperationException(failure + $" Text=[{_editor.Text}] Caret={_editor.CaretIndex}.");
    }

    private static string WaitForOverlayReady()
    {
        string? clientId = null;
        WaitFor(() =>
        {
            string diagnostics = ReadLogTail(_tsfDiagnostics);
            string marker = $"TSF pid={Environment.ProcessId} client.connected ";
            int markerIndex = diagnostics.LastIndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0) return false;
            int valueStart = markerIndex + marker.Length;
            int valueEnd = diagnostics.IndexOfAny(['\r', '\n'], valueStart);
            clientId = diagnostics[valueStart..(valueEnd < 0 ? diagnostics.Length : valueEnd)].Trim();
            return clientId.StartsWith("host-", StringComparison.Ordinal);
        }, "The installed TSF did not receive the Overlay ready handshake.");
        return clientId!;
    }

    private static void WaitForOverlayWindow(string title, string failure)
    {
        WaitFor(() => IsOverlayWindowVisible(title), failure);
    }

    private static bool IsOverlayWindowVisible(string expectedTitle)
    {
        bool found = false;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || GetWindowTextLengthW(window) != expectedTitle.Length) return true;
            var title = new StringBuilder(expectedTitle.Length + 1);
            GetWindowTextW(window, title, title.Capacity);
            if (!string.Equals(title.ToString(), expectedTitle, StringComparison.Ordinal)) return true;
            GetWindowThreadProcessId(window, out uint processId);
            try
            {
                using Process process = Process.GetProcessById((int)processId);
                if (!string.Equals(process.ProcessName, "EnputMethod.Overlay", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (ArgumentException)
            {
                return true;
            }
            if (!GetWindowRect(window, out NativeRect bounds) || bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top) return true;
            found = true;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static void VerifyOverlayDiagnosticEvidence()
    {
        string tsfDiagnostics = ReadLogTail(_tsfDiagnostics);
        string processMarker = $"TSF pid={Environment.ProcessId} ";
        string[] processLines = tsfDiagnostics.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains(processMarker, StringComparison.Ordinal)).ToArray();
        if (!processLines.Any(line => line.Contains($"client.connected {_overlayClientId}", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("TSF diagnostics did not retain the current Overlay ready handshake.");
        }
        string[] publishedStates = processLines
            .Select(line => ValueAfter(line, "candidate.published state="))
            .Where(state => state is not null)
            .Cast<string>()
            .ToArray();
        if (publishedStates.Length == 0)
        {
            throw new InvalidOperationException("TSF diagnostics did not record a candidate publication for the current host.");
        }
        if (processLines.Any(line => line.Contains("candidate.skipped overlay-not-connected", StringComparison.Ordinal) ||
                                     line.Contains("candidate.skipped publish-failed", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("TSF diagnostics recorded an Overlay publication failure during the uninterrupted test run.");
        }

        string overlayDiagnostics = ReadLogTail(_overlayDiagnostics);
        if (!overlayDiagnostics.Contains($"pipe.connected {_overlayClientId}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("WPF diagnostics did not retain the current Overlay client connection.");
        }
        string[] relevantOverlayLines = overlayDiagnostics.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains(_overlayClientId, StringComparison.Ordinal)).ToArray();
        bool candidateStateReachedWpf = publishedStates.Any(state =>
            relevantOverlayLines.Any(line => line.Contains($"pipe.received showCandidates state={state} ", StringComparison.Ordinal)) &&
            relevantOverlayLines.Any(line => line.Contains($"candidate.presented state={state} ", StringComparison.Ordinal)));
        bool translationStateReachedWpf = publishedStates.Any(state =>
            relevantOverlayLines.Any(line => line.Contains($"pipe.received showTranslation state={state} ", StringComparison.Ordinal)));
        if (!candidateStateReachedWpf || !translationStateReachedWpf)
        {
            throw new InvalidOperationException("WPF diagnostics did not match a candidate and translation state published by the current TSF client.");
        }
    }

    private static string? ValueAfter(string line, string marker)
    {
        int markerIndex = line.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0) return null;
        int valueStart = markerIndex + marker.Length;
        int valueEnd = line.IndexOf(' ', valueStart);
        return line[valueStart..(valueEnd < 0 ? line.Length : valueEnd)];
    }

    private static string CaptureCandidateFrequencyRegistry()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(CandidateFrequencyRegistryPath, writable: false);
        if (key is null) return "<missing>";
        var snapshot = new StringBuilder();
        foreach (string name in key.GetValueNames().OrderBy(value => value, StringComparer.Ordinal))
        {
            RegistryValueKind kind = key.GetValueKind(name);
            object? value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            snapshot.Append(Convert.ToBase64String(Encoding.Unicode.GetBytes(name))).Append('|').Append(kind).Append('|');
            snapshot.Append(value switch
            {
                byte[] bytes => Convert.ToBase64String(bytes),
                string[] strings => string.Join("\u001f", strings.Select(item => Convert.ToBase64String(Encoding.Unicode.GetBytes(item)))),
                null => "<null>",
                _ => Convert.ToString(value, CultureInfo.InvariantCulture),
            }).AppendLine();
        }
        return snapshot.ToString();
    }

    private static LogCheckpoint CaptureLogCheckpoint(string path)
    {
        try { return new LogCheckpoint(path, File.Exists(path) ? new FileInfo(path).Length : 0); }
        catch (IOException) { return new LogCheckpoint(path, 0); }
    }

    private static string ReadLogTail(LogCheckpoint checkpoint)
    {
        try
        {
            using var stream = new FileStream(checkpoint.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(Math.Min(checkpoint.Offset, stream.Length), SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (FileNotFoundException)
        {
            return string.Empty;
        }
        catch (DirectoryNotFoundException)
        {
            return string.Empty;
        }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    private readonly record struct LogCheckpoint(string Path, long Offset);

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char character);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachInput);

    private sealed class TestInterferenceException(string message) : InvalidOperationException(message);
}
