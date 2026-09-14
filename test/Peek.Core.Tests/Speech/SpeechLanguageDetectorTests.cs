using Peek.Core.Services.Speech;
using Xunit;

namespace Peek.Core.Tests.Speech;

public class SpeechLanguageDetectorTests
{
    [Fact]
    public void Empty_text_produces_no_segments()
    {
        Assert.Empty(SpeechLanguageDetector.Segment("", "en"));
    }

    [Fact]
    public void Pure_latin_text_is_one_segment_in_the_primary_language()
    {
        var segments = SpeechLanguageDetector.Segment("Hello world", "en");

        var segment = Assert.Single(segments);
        Assert.Equal("Hello world", segment.Text);
        Assert.Equal("en", segment.Language);
    }

    [Fact]
    public void Pure_latin_text_falls_back_to_whichever_primary_language_is_configured()
    {
        var segments = SpeechLanguageDetector.Segment("Guten Tag", "de");

        var segment = Assert.Single(segments);
        Assert.Equal("de", segment.Language);
    }

    [Fact]
    public void Pure_chinese_text_is_always_tagged_zh_regardless_of_primary_language()
    {
        var segments = SpeechLanguageDetector.Segment("你好世界", "en");

        var segment = Assert.Single(segments);
        Assert.Equal("你好世界", segment.Text);
        Assert.Equal("zh", segment.Language);
    }

    [Fact]
    public void Mixed_text_splits_into_runs_by_script()
    {
        var segments = SpeechLanguageDetector.Segment("Hello 你好 world", "en");

        Assert.Collection(segments,
            s => { Assert.Equal("Hello ", s.Text); Assert.Equal("en", s.Language); },
            s => { Assert.Equal("你好 ", s.Text); Assert.Equal("zh", s.Language); },
            s => { Assert.Equal("world", s.Text); Assert.Equal("en", s.Language); });
    }

    [Fact]
    public void Punctuation_between_two_chinese_runs_does_not_force_a_language_switch()
    {
        var segments = SpeechLanguageDetector.Segment("你好，世界！", "en");

        var segment = Assert.Single(segments);
        Assert.Equal("你好，世界！", segment.Text);
        Assert.Equal("zh", segment.Language);
    }

    [Fact]
    public void Leading_punctuation_before_any_letter_takes_the_primary_language()
    {
        var segments = SpeechLanguageDetector.Segment("... hello", "en");

        var segment = Assert.Single(segments);
        Assert.Equal("en", segment.Language);
    }

    [Theory]
    [InlineData("en-US", "en")]
    [InlineData("zh-CN", "zh")]
    [InlineData("de-DE", "de")]
    [InlineData("en", "en")]
    public void ToLanguageCode_reduces_bcp47_culture_to_two_letter_code(string bcp47, string expected)
    {
        Assert.Equal(expected, SpeechLanguageDetector.ToLanguageCode(bcp47));
    }
}
