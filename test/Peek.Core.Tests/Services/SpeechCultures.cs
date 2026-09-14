global using static Peek.Core.Tests.Services.SpeechCultures;

using System.Globalization;

namespace Peek.Core.Tests.Services;

/// <summary>
/// Cultures for the announcement-formatter tests. The formatters now phrase announcements in
/// the speech culture (see SpeechStrings), so every call needs one; the existing assertions
/// are all in English, which is what the neutral resource holds - hence Invariant.
/// </summary>
internal static class SpeechCultures
{
    /// <summary>Resolves to the neutral (English) resource entries.</summary>
    public static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static readonly CultureInfo Chinese = CultureInfo.GetCultureInfo("zh-CN");
}
