using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.Screenshot;

namespace Peek.Worker.Screenshot.Windows;

public sealed class WindowsScreenshotService : IScreenshotService
{
    private readonly IImageSimilarityAlgorithm _similarity;
    private readonly ILogger<WindowsScreenshotService> _logger;

    public WindowsScreenshotService(IImageSimilarityAlgorithm similarity, ILogger<WindowsScreenshotService> logger)
    {
        _similarity = similarity;
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
            using var bitmap = CaptureWindowBitmap(hwnd);
            return Encode(bitmap);
        }, ct);

    public Task<ScreenshotFingerprintResult> CaptureFingerprintAsync(nint hwnd, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var bitmap = CaptureWindowBitmap(hwnd);
            var bgraPixels = ExtractBgraPixels(bitmap);
            var fingerprint = _similarity.ComputeFingerprint(bgraPixels, bitmap.Width, bitmap.Height);
            return new ScreenshotFingerprintResult { Fingerprint = fingerprint };
        }, ct);

    /// <summary>
    /// Shared by <see cref="CaptureWindowAsync"/> and <see cref="CaptureFingerprintAsync"/> -
    /// both need the exact same pixels, just encoded differently afterwards, and the capture
    /// itself (not the encoding) is the expensive part (PrintWindow/CopyFromScreen).
    /// </summary>
    private Bitmap CaptureWindowBitmap(nint hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            throw new InvalidOperationException($"GetWindowRect failed for handle 0x{hwnd:X}.");

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"Window 0x{hwnd:X} has an empty rect.");

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
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

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

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

    /// <summary>
    /// Raw BGRA8888 bytes for <see cref="IImageSimilarityAlgorithm.ComputeFingerprint"/> - via
    /// <see cref="Bitmap.LockBits"/> rather than a per-pixel <see cref="Bitmap.GetPixel"/> loop,
    /// which is well known to be slow enough to matter at full window resolution (this can run
    /// against a 1000x1000+ image every few seconds while a window is hovered).
    /// </summary>
    private static byte[] ExtractBgraPixels(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = bitmap.Width * 4;
            var pixels = new byte[rowBytes * bitmap.Height];

            if (data.Stride == rowBytes)
            {
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            }
            else
            {
                // Stride can exceed rowBytes (row padding for alignment) - copy row by row
                // rather than assuming a tightly-packed buffer.
                for (var y = 0; y < bitmap.Height; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * rowBytes, rowBytes);
            }

            return pixels;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
