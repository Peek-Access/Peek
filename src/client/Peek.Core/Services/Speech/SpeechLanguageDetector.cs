namespace Peek.Core.Services.Speech;

/// <summary>One contiguous run of text tagged with the two-letter language code it should be spoken in.</summary>
public readonly record struct SpeechSegment(string Text, string Language);

/// <summary>
/// Splits mixed-language text into runs so each can be synthesized with the matching Piper
/// voice (see AccessibilitySpeechService). Detection is Unicode-range based, which is
/// reliable for Chinese (CJK characters have their own Unicode blocks) but - as the user
/// requested - cannot reliably tell German and English apart, since both use the plain
/// Latin alphabet. Anything that isn't determinably Chinese is spoken in the user's chosen
/// primary language instead of guessing.
/// </summary>
public static class SpeechLanguageDetector
{
    public const string Chinese = "zh";

    /// <summary>
    /// Splits <paramref name="text"/> into language-tagged runs. Digits, whitespace and
    /// punctuation carry no language signal on their own and are folded into whichever run
    /// they trail, so "Hi 你好, world!" doesn't fragment into a run per punctuation mark.
    /// </summary>
    public static IReadOnlyList<SpeechSegment> Segment(string text, string primaryLanguage)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var languages = new string[text.Length];
        var lastResolved = primaryLanguage;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (IsChineseCharacter(c))
                lastResolved = Chinese;
            else if (char.IsLetter(c))
                lastResolved = primaryLanguage;
            // else: digit/whitespace/punctuation/symbol - no language signal, keep lastResolved.

            languages[i] = lastResolved;
        }

        var segments = new List<SpeechSegment>();
        var runStart = 0;
        for (var i = 1; i <= text.Length; i++)
        {
            if (i == text.Length || languages[i] != languages[runStart])
            {
                segments.Add(new SpeechSegment(text[runStart..i], languages[runStart]));
                runStart = i;
            }
        }

        return segments;
    }

    /// <summary>Reduces a BCP-47 culture name ("en-US", "zh-CN", "de-DE") to the two-letter code <see cref="Segment"/> and Piper voice keys use.</summary>
    public static string ToLanguageCode(string bcp47Culture)
    {
        var separator = bcp47Culture.IndexOf('-');
        var code = separator < 0 ? bcp47Culture : bcp47Culture[..separator];
        return code.ToLowerInvariant();
    }

    private static bool IsChineseCharacter(char c) =>
        (c >= '一' && c <= '鿿') ||  // CJK Unified Ideographs
        (c >= '㐀' && c <= '䶿') ||  // CJK Unified Ideographs Extension A
        (c >= '　' && c <= '〿') ||  // CJK punctuation (、。《》「」etc.)
        (c >= '＀' && c <= '￯');    // Halfwidth/fullwidth forms
}
