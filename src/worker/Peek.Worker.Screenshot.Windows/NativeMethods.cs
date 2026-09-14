using System.Runtime.InteropServices;

namespace Peek.Worker.Screenshot.Windows;

internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public const uint PwRenderFullContent = 0x00000002;

    public const int SmXVirtualScreen = 76;
    public const int SmYVirtualScreen = 77;
    public const int SmCxVirtualScreen = 78;
    public const int SmCyVirtualScreen = 79;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hWnd, out Rect lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PrintWindow(nint hWnd, nint hdcBlt, uint nFlags);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int nIndex);
}
