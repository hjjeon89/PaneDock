using System.Runtime.InteropServices;

namespace PaneDock.App.Controls;

internal static class WindowWorkArea
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 2;

    // A borderless WindowChrome window otherwise maximizes to the entire
    // monitor. Use native pixel coordinates, not the primary screen's WPF
    // work area, so taskbars on other edges/monitors are respected as well.
    internal static nint ConstrainMaximize(nint hwnd, int message, nint wParam,
        nint lParam, ref bool handled)
    {
        if (message != WmGetMinMaxInfo || lParam == 0)
            return 0;

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
            return 0;

        var bounds = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        if (!ApplyWorkArea(ref bounds, info.Monitor, info.Work))
            return 0;

        Marshal.StructureToPtr(bounds, lParam, false);
        return 0;
    }

    internal static bool ApplyWorkArea(ref MinMaxInfo bounds, NativeRect monitor, NativeRect work)
    {
        var width = work.Right - work.Left;
        var height = work.Bottom - work.Top;
        if (width <= 0 || height <= 0)
            return false;

        bounds.MaxPosition = new(work.Left - monitor.Left, work.Top - monitor.Top);
        bounds.MaxSize = new(width, height);
        // Keep min/max tracking constraints for ordinary resizing unchanged.
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal record struct NativePoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    internal record struct NativeRect(int Left, int Top, int Right, int Bottom);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
