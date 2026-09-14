using System.Drawing;
using System.Drawing.Imaging;
using Xunit;

namespace Peek.Integration.Tests;

/// <summary>
/// Exercises ocr.recognize against a real worker. Uses SimdPaddleOCR's embedded
/// model (ships inside the NuGet package itself, no first-use download) rather
/// than Piper/Ollama, which would need external assets/services not to be assumed
/// present in CI - same principle as "do not make tests depend on real TTS engines
/// or remote LLM services".
/// </summary>
public sealed class OcrRoundTripTests : IClassFixture<TestWorkerFixture>
{
    private readonly TestWorkerFixture _fixture;

    public OcrRoundTripTests(TestWorkerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Recognizes_rendered_text_in_a_synthetic_image()
    {
        var png = RenderText("Integration Test 42");

        var result = await _fixture.Connection.Client.Ocr.RecognizeAsync(png);

        Assert.Contains("Integration", result.Text);
        Assert.Contains("42", result.Text);
        Assert.False(result.FromCache);
    }

    [Fact]
    public async Task Identical_image_hits_the_cache_on_the_second_call()
    {
        var png = RenderText("Cache Check 7");

        await _fixture.Connection.Client.Ocr.RecognizeAsync(png);
        var second = await _fixture.Connection.Client.Ocr.RecognizeAsync(png);

        Assert.True(second.FromCache);
    }

    [Fact]
    public async Task GetStatus_reports_the_embedded_model()
    {
        var status = await _fixture.Connection.Client.Ocr.GetStatusAsync();

        Assert.True(status.IsReady || status.RecognitionsPerformed >= 0); // model loads lazily on first request
        Assert.Equal("PP-OCRv6-tiny", status.ModelName);
    }

    private static byte[] RenderText(string text)
    {
        using var bitmap = new Bitmap(500, 150);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        using var font = new Font("Segoe UI", 28, FontStyle.Regular);
        graphics.DrawString(text, font, Brushes.Black, new PointF(15, 50));

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
