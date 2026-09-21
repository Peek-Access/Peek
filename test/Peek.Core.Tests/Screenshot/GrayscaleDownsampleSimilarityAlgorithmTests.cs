using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Peek.Worker.Screenshot;
using Xunit;

namespace Peek.Core.Tests.Screenshot;

/// <summary>
/// Pins IImageSimilarityAlgorithm's default implementation against real, code-generated
/// images (solid fills, a placed rectangle, a half/half split) rather than hand-picked byte
/// arrays, so each assertion can be derived from what was actually drawn - not just from
/// re-stating the implementation. Complements ScreenshotFingerprintTests (which measures the
/// same algorithm against a real worker and a real window) by pinning down the algorithm's
/// exact, predictable behavior on inputs whose expected fingerprint is known in advance.
/// </summary>
public class GrayscaleDownsampleSimilarityAlgorithmTests
{
    // Exact multiples of the algorithm's 32x24 fingerprint grid (see
    // GrayscaleDownsampleSimilarityAlgorithm.FingerprintWidth/Height) - every target cell then
    // maps to a clean, non-fractional 10x10 block of source pixels, so tests can predict exact
    // fingerprint values instead of just bounding them.
    private const int Width = 320;
    private const int Height = 240;
    private const int CellPixels = 10; // Width / FingerprintWidth == Height / FingerprintHeight

    private readonly IImageSimilarityAlgorithm _algorithm = new GrayscaleDownsampleSimilarityAlgorithm();

    [Fact]
    public void Fingerprint_is_always_32_by_24_regardless_of_source_size()
    {
        var small = RenderBgra(8, 8, _ => { });
        var large = RenderBgra(1920, 1080, _ => { });

        var smallFingerprint = _algorithm.ComputeFingerprint(small, 8, 8);
        var largeFingerprint = _algorithm.ComputeFingerprint(large, 1920, 1080);

        var expectedLength = GrayscaleDownsampleSimilarityAlgorithm.FingerprintWidth
            * GrayscaleDownsampleSimilarityAlgorithm.FingerprintHeight;
        Assert.Equal(expectedLength, smallFingerprint.Length);
        Assert.Equal(expectedLength, largeFingerprint.Length);
    }

    [Fact]
    public void A_solid_white_image_fingerprints_to_all_255()
    {
        var pixels = RenderBgra(Width, Height, g => g.Clear(Color.White));

        var fingerprint = _algorithm.ComputeFingerprint(pixels, Width, Height);

        Assert.All(fingerprint, b => Assert.Equal(255, b));
    }

    [Fact]
    public void A_solid_black_image_fingerprints_to_all_zero()
    {
        var pixels = RenderBgra(Width, Height, g => g.Clear(Color.Black));

        var fingerprint = _algorithm.ComputeFingerprint(pixels, Width, Height);

        Assert.All(fingerprint, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Identical_images_have_zero_change_ratio()
    {
        var pixelsA = RenderBgra(Width, Height, g => DrawCheckerboard(g));
        var pixelsB = RenderBgra(Width, Height, g => DrawCheckerboard(g));

        var fingerprintA = _algorithm.ComputeFingerprint(pixelsA, Width, Height);
        var fingerprintB = _algorithm.ComputeFingerprint(pixelsB, Width, Height);

        Assert.Equal(0.0, _algorithm.ComputeChangeRatio(fingerprintA, fingerprintB));
    }

    [Fact]
    public void White_versus_black_is_maximally_different()
    {
        var white = RenderBgra(Width, Height, g => g.Clear(Color.White));
        var black = RenderBgra(Width, Height, g => g.Clear(Color.Black));

        var whiteFingerprint = _algorithm.ComputeFingerprint(white, Width, Height);
        var blackFingerprint = _algorithm.ComputeFingerprint(black, Width, Height);

        Assert.Equal(1.0, _algorithm.ComputeChangeRatio(whiteFingerprint, blackFingerprint));
    }

    [Fact]
    public void A_single_cell_sized_black_square_changes_exactly_one_fingerprint_cell()
    {
        // The square is aligned exactly to fingerprint cell (0,0)'s source block (see CellPixels),
        // so of the 768 (32*24) fingerprint bytes, exactly one should flip from 255 to 0 and
        // every other one should stay untouched at 255 - a change ratio this test can predict
        // exactly, not just bound: (1 cell * 255 intensity delta) / (768 cells * 255 range).
        var before = RenderBgra(Width, Height, g => g.Clear(Color.White));
        var after = RenderBgra(Width, Height, g =>
        {
            g.Clear(Color.White);
            g.SmoothingMode = SmoothingMode.None;
            g.FillRectangle(Brushes.Black, 0, 0, CellPixels, CellPixels);
        });

        var beforeFingerprint = _algorithm.ComputeFingerprint(before, Width, Height);
        var afterFingerprint = _algorithm.ComputeFingerprint(after, Width, Height);

        // Confirms the change really did land on exactly one cell, as constructed, before
        // trusting the derived ratio below.
        var changedCells = 0;
        for (var i = 0; i < beforeFingerprint.Length; i++)
            if (beforeFingerprint[i] != afterFingerprint[i]) changedCells++;
        Assert.Equal(1, changedCells);
        Assert.Equal(0, afterFingerprint[0]); // cell (0,0) - top-left, where the square was drawn

        var totalCells = GrayscaleDownsampleSimilarityAlgorithm.FingerprintWidth
            * GrayscaleDownsampleSimilarityAlgorithm.FingerprintHeight;
        var expectedRatio = 255.0 / totalCells / 255.0; // one full-range cell out of `totalCells`

        var ratio = _algorithm.ComputeChangeRatio(beforeFingerprint, afterFingerprint);
        Assert.Equal(expectedRatio, ratio, precision: 10);
    }

    [Fact]
    public void A_vertical_split_fingerprints_each_half_to_its_own_color()
    {
        // Split exactly on a fingerprint column boundary (160 == 16 cells * CellPixels), so
        // there's no partially-covered cell to blur the two halves together.
        var pixels = RenderBgra(Width, Height, g =>
        {
            g.SmoothingMode = SmoothingMode.None;
            g.FillRectangle(Brushes.Black, 0, 0, Width / 2, Height);
            g.FillRectangle(Brushes.White, Width / 2, 0, Width / 2, Height);
        });

        var fingerprint = _algorithm.ComputeFingerprint(pixels, Width, Height);

        for (var ty = 0; ty < GrayscaleDownsampleSimilarityAlgorithm.FingerprintHeight; ty++)
        {
            for (var tx = 0; tx < GrayscaleDownsampleSimilarityAlgorithm.FingerprintWidth; tx++)
            {
                var value = fingerprint[ty * GrayscaleDownsampleSimilarityAlgorithm.FingerprintWidth + tx];
                var expected = tx < GrayscaleDownsampleSimilarityAlgorithm.FingerprintWidth / 2 ? 0 : 255;
                Assert.Equal(expected, value);
            }
        }
    }

    [Fact]
    public void Mismatched_fingerprint_lengths_fail_closed_to_fully_changed()
    {
        byte[] before = [1, 2, 3];
        byte[] after = [1, 2];

        Assert.Equal(1.0, _algorithm.ComputeChangeRatio(before, after));
    }

    [Fact]
    public void Empty_fingerprints_fail_closed_to_fully_changed()
    {
        Assert.Equal(1.0, _algorithm.ComputeChangeRatio([], []));
    }

    [Fact]
    public void Non_positive_dimensions_are_rejected()
    {
        var pixels = new byte[4];

        Assert.Throws<ArgumentException>(() => _algorithm.ComputeFingerprint(pixels, 0, 1));
        Assert.Throws<ArgumentException>(() => _algorithm.ComputeFingerprint(pixels, 1, 0));
    }

    [Fact]
    public void A_pixel_buffer_smaller_than_width_times_height_times_4_is_rejected()
    {
        var pixels = new byte[10 * 10 * 4 - 1]; // one byte short of a full 10x10 BGRA buffer

        Assert.Throws<ArgumentException>(() => _algorithm.ComputeFingerprint(pixels, 10, 10));
    }

    private static void DrawCheckerboard(Graphics g)
    {
        g.Clear(Color.White);
        g.SmoothingMode = SmoothingMode.None;
        const int square = 20;
        for (var y = 0; y < Height; y += square)
            for (var x = 0; x < Width; x += square)
                if (((x / square) + (y / square)) % 2 == 0)
                    g.FillRectangle(Brushes.Black, x, y, square, square);
    }

    /// <summary>
    /// Draws via <paramref name="draw"/> onto a fresh BGRA32 bitmap, then extracts its raw
    /// pixel bytes the same way WindowsScreenshotService.ExtractBgraPixels does in production
    /// - so these tests exercise the algorithm against the exact byte layout it's actually fed.
    /// </summary>
    private static byte[] RenderBgra(int width, int height, Action<Graphics> draw)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
            draw(g);

        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = width * 4;
            var pixels = new byte[rowBytes * height];
            if (data.Stride == rowBytes)
            {
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            }
            else
            {
                for (var y = 0; y < height; y++)
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
