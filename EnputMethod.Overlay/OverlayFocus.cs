using System.Runtime.InteropServices;

namespace EnputMethod.Overlay;

internal static class OverlayFocus
{
    private const uint GaRoot = 2;

    internal static bool IsForegroundWindow(long ownerWindow)
    {
        if (ownerWindow == 0) return true;

        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;

        IntPtr ownerRoot = GetAncestor(new IntPtr(ownerWindow), GaRoot);
        IntPtr foregroundRoot = GetAncestor(foreground, GaRoot);
        return ownerRoot != IntPtr.Zero && ownerRoot == foregroundRoot;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);
}
