
namespace Peek.Core.Models;

public sealed record WindowNode
{
    public nint Hwnd { get; init; }
    public nint ParentHwnd { get; init; }
    public nint OwnerHwnd { get; init; }

    public string Title { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;

    public uint ProcessId { get; init; }
    public uint ThreadId { get; init; }
    public string ProcessName { get; init; } = string.Empty;

    public WindowRect Rect { get; init; } = WindowRect.Empty;

    public uint Style { get; init; }
    public uint ExStyle { get; init; }

    public bool IsVisible { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsMinimized { get; init; }
    public bool IsMaximized { get; init; }
    public bool IsTopmost { get; init; }
    public bool IsToolWindow { get; init; }
    public bool IsRoot { get; init; }
    public int Depth { get; init; }
    public IReadOnlyList<WindowNode> Children { get; init; } = [];
}

public sealed record WindowRect(int Left, int Top, int Width, int Height)
{
    public static readonly WindowRect Empty = new(0, 0, 0, 0);
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}
