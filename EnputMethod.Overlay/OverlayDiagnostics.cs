using System.Diagnostics;
using System.IO;
using System.Text;

namespace EnputMethod.Overlay;

internal static class OverlayDiagnostics
{
    private const string MutexName = "Local\\EnputMethod.OverlayDiagnostics.v1";

    internal static void Write(string @event, string detail = "")
    {
        try
        {
            using var mutex = new Mutex(false, MutexName);
            if (!mutex.WaitOne(TimeSpan.FromMilliseconds(25))) return;
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Enput Method");
                Directory.CreateDirectory(directory);
                string line = $"{DateTimeOffset.Now:O} WPF pid={Environment.ProcessId} {@event} {detail}{Environment.NewLine}";
                File.AppendAllText(Path.Combine(directory, "overlay-diagnostics.log"), line, new UTF8Encoding(false));
                Debug.Write(line);
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
        catch (AbandonedMutexException)
        {
            // Diagnostics must never affect candidate presentation.
        }
        catch (IOException)
        {
            // Diagnostics must never affect candidate presentation.
        }
        catch (UnauthorizedAccessException)
        {
            // Diagnostics must never affect candidate presentation.
        }
        catch (ObjectDisposedException)
        {
            // Diagnostics must never affect candidate presentation.
        }
    }
}
