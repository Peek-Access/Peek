using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.Screenshot;

namespace Peek.Worker.Screenshot.Windows;

public sealed class WindowsScreenshotService : IScreenshotService
{
    private readonly ILogger<WindowsScreenshotService> _logger;

    public WindowsScreenshotService(ILogger<WindowsScreenshotService> logger)
    {
        _logger = logger;
    }

    public Task<ScreenshotResult> CaptureDesktopAsync(CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var x = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
            var y = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
            var width = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
            var height = NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen);

            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(x, y, 0, 0, bitmap.Size);
            }

            return Encode(bitmap);
        }, ct);

    public Task<ScreenshotResult> CaptureWindowAsync(nint hwnd, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            if (!NativeMethods.GetWindowRect(hwnd, out var rect))
                throw new InvalidOperationException($"GetWindowRect failed for handle 0x{hwnd:X}.");

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException($"Window 0x{hwnd:X} has an empty rect.");

            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                var hdc = graphics.GetHdc();
                bool printed;
                try
                {
                    printed = NativeMethods.PrintWindow(hwnd, hdc, NativeMethods.PwRenderFullContent);
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }

                if (!printed)
                {
                    // PrintWindow can fail for some legacy/GDI-only windows. Fall
                    // back to a plain screen copy of the window's bounds - this
                    // only captures whatever is currently on-screen (misses
                    // occluded regions), so PrintWindow above is the primary,
                    // more-correct path and this is a best-effort fallback.
                    _logger.LogDebug("PrintWindow failed for 0x{Hwnd:X} - falling back to CopyFromScreen", hwnd);
                    graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size);
                }
            }

            return Encode(bitmap);
        }, ct);

    private static ScreenshotResult Encode(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return new ScreenshotResult
        {
            ImageData = stream.ToArray(),
            Width = bitmap.Width,
            Height = bitmap.Height,
        };
    }
}
