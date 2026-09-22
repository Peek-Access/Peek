using System.Globalization;
using Peek.Core.i18n;
using Xunit;

namespace Peek.Core.Tests.i18n;

public class SystemLanguageResolverTests
{
    [Theory]
    [InlineData("de-DE", "de-DE")]
    [InlineData("de-AT", "de-DE")]
    [InlineData("de-CH", "de-DE")]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("zh-SG", "zh-CN")]
    [InlineData("en-US", "en-US")]
    [InlineData("en-GB", "en-US")]
    public void Matches_the_supported_culture_for_the_same_language(string systemCulture, string expected)
    {
        Assert.Equal(expected, SystemLanguageResolver.ResolveDefaultUiLanguage(CultureInfo.GetCultureInfo(systemCulture)));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("ja-JP")]
    [InlineData("es-ES")]
    public void Falls_back_to_english_for_an_unsupported_language(string systemCulture)
    {
        Assert.Equal("en-US", SystemLanguageResolver.ResolveDefaultUiLanguage(CultureInfo.GetCultureInfo(systemCulture)));
    }
}
