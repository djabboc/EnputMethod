using System.Runtime.InteropServices;

namespace EnputMethod.Overlay;

internal static class OverlayFocus
{
    private const uint GaRoot = 2;

    internal static bool IsForegroundWindow(long ownerWindow)
    {
        return IsOwnerOfForeground(ownerWindow, GetForegroundWindow());
    }

    internal static bool IsOwnerOfForeground(long ownerWindow, IntPtr foreground)
    {
        if (ownerWindow == 0) return true;
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
