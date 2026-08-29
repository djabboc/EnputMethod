using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EnputMethod.Overlay;

internal static class OverlayPositioning
{
    private const uint MonitorDefaultToNearest = 2;

    internal static Point Constrain(Window window, int x, int y)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return new Point(x, y);

        uint dpi = GetDpiForWindow(handle);
        double scale = 96.0 / dpi;
        window.UpdateLayout();
        int width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth / scale));
        int height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight / scale));
        IntPtr monitor = MonitorFromPoint(new NativePoint(x, y), MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            x = Math.Clamp(x, info.WorkArea.Left, Math.Max(info.WorkArea.Left, info.WorkArea.Right - width));
            y = Math.Clamp(y, info.WorkArea.Top, Math.Max(info.WorkArea.Top, info.WorkArea.Bottom - height));
        }
        return new Point(x * scale, y * scale);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        internal int Size;
        internal NativeRect MonitorArea;
        internal NativeRect WorkArea;
        internal uint Flags;
    }
}
