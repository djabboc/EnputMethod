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

    internal static Point NearComposition(Window window, Rect compositionBounds)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return new Point(compositionBounds.Left, compositionBounds.Bottom + 2);

        uint dpi = GetDpiForWindow(handle);
        double scale = 96.0 / dpi;
        window.UpdateLayout();
        int width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth / scale));
        int height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight / scale));
        int centerX = (int)Math.Round(compositionBounds.Left + compositionBounds.Width / 2);
        int centerY = (int)Math.Round(compositionBounds.Top + compositionBounds.Height / 2);
        IntPtr monitor = MonitorFromPoint(new NativePoint(centerX, centerY), MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return Constrain(window, (int)Math.Round(compositionBounds.Left), (int)Math.Round(compositionBounds.Bottom) + 2);
        }

        Point position = PlaceCandidate(compositionBounds, new Size(width, height), new Rect(info.WorkArea.Left, info.WorkArea.Top, info.WorkArea.Right - info.WorkArea.Left, info.WorkArea.Bottom - info.WorkArea.Top));
        return new Point(position.X * scale, position.Y * scale);
    }

    // Prefer the side of the composition that keeps the complete candidate surface visible.
    // The final fallback is only for a candidate surface taller than both available sides.
    internal static Point PlaceCandidate(Rect compositionBounds, Size candidateSize, Rect workArea)
    {
        double width = Math.Max(1, Math.Ceiling(candidateSize.Width));
        double height = Math.Max(1, Math.Ceiling(candidateSize.Height));
        const double gap = 2;
        double x = Math.Clamp(compositionBounds.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        double below = compositionBounds.Bottom + gap;
        double above = compositionBounds.Top - gap - height;
        bool belowFits = below + height <= workArea.Bottom;
        bool aboveFits = above >= workArea.Top;
        double y;
        if (belowFits)
        {
            y = below;
        }
        else if (aboveFits)
        {
            y = above;
        }
        else
        {
            double aboveSpace = Math.Max(0, compositionBounds.Top - gap - workArea.Top);
            double belowSpace = Math.Max(0, workArea.Bottom - compositionBounds.Bottom - gap);
            y = aboveSpace >= belowSpace
                ? Math.Max(workArea.Top, above)
                : Math.Min(workArea.Bottom - height, below);
        }
        return new Point(x, y);
    }

    internal static Rect? ScreenBounds(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !window.IsVisible) return null;
        double scale = 96.0 / GetDpiForWindow(handle);
        window.UpdateLayout();
        double width = window.ActualWidth / scale;
        double height = window.ActualHeight / scale;
        if (width <= 0 || height <= 0) return null;
        return new Rect(window.Left / scale, window.Top / scale, width, height);
    }
    internal static Point Adjacent(Window window, Rect candidateBounds)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return new Point(candidateBounds.Right + 12, candidateBounds.Top);
        double scale = 96.0 / GetDpiForWindow(handle);
        window.UpdateLayout();
        int width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth / scale));
        int height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight / scale));
        int x = (int)Math.Ceiling(candidateBounds.Right) + 12;
        int y = (int)Math.Floor(candidateBounds.Top);
        IntPtr monitor = MonitorFromPoint(new NativePoint(x, y), MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            bool rightFits = x + width <= info.WorkArea.Right;
            bool leftFits = candidateBounds.Left - 12 - width >= info.WorkArea.Left;
            if (!rightFits && leftFits) x = (int)Math.Floor(candidateBounds.Left) - 12 - width;
            else if (!rightFits)
            {
                x = (int)Math.Floor(candidateBounds.Left);
                if (candidateBounds.Bottom + 12 + height <= info.WorkArea.Bottom) y = (int)Math.Ceiling(candidateBounds.Bottom) + 12;
                else y = (int)Math.Floor(candidateBounds.Top) - 12 - height;
            }
        }
        return Constrain(window, x, y);
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
