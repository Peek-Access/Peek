using System.Runtime.InteropServices;

namespace Peek.Worker.Automation.Windows;

internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(Point point);

    public static nint GetWindowHandleAt(int x, int y) =>
        WindowFromPoint(new Point { X = x, Y = y });
}
