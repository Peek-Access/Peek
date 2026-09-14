using System.Globalization;
using Peek.Core.Services;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using Peek.Worker.Contracts.Automation;
using Xunit;

namespace Peek.Core.Tests.Services;

/// <summary>
/// Spoken output is the primary product surface of a screen reader, so it gets the same
/// treatment the UI does: translated, and translated by the <em>speech</em> language rather
/// than the UI language. These tests exist because that distinction is invisible at a glance
/// and easy to regress back into "just use the current UI culture".
/// </summary>
public sealed class SpeechStringsTests
{
    [Fact]
    public void Speech_culture_prefers_the_tts_language_over_the_ui_language()
    {
        var localization = new LocalizationSettings { UiLanguage = "en-US", TtsLanguage = "de-DE" };

        Assert.Equal("de-DE", SpeechStrings.ResolveCulture(localization).Name);
    }

    [Fact]
    public void Speech_culture_falls_back_to_the_ui_language_when_no_tts_language_is_set()
    {
        var localization = new LocalizationSettings { UiLanguage = "zh-CN", TtsLanguage = "" };

        Assert.Equal("zh-CN", SpeechStrings.ResolveCulture(localization).Name);
    }

    [Fact]
    public void A_malformed_culture_name_does_not_throw()
    {
        // A hand-edited settings.json shouldn't be able to take speech down entirely -
        // for this app that failure mode is silence, with no way to hear what went wrong.
        var localization = new LocalizationSettings { UiLanguage = "not-a-culture", TtsLanguage = "also-not-one" };

        var culture = SpeechStrings.ResolveCulture(localization);

        Assert.NotNull(culture);
    }

    [Fact]
    public void Element_state_is_spoken_in_the_speech_language()
    {
        var policy = new StandardSpeechPolicy();
        var element = new SemanticElement
        {
            Name = "Remember me",
            ControlType = "CheckBox",
            LocalizedControlType = "check box",
            IsEnabled = true,
            ToggleState = ToggleState.On,
        };

        var english = policy.Describe(element, SpeechVerbosity.Standard, Invariant).Text;
        var german = policy.Describe(element, SpeechVerbosity.Standard, German).Text;

        Assert.Contains("checked", english);
        Assert.Contains("aktiviert", german);
        // The element's own name comes from the inspected app - never translated.
        Assert.Contains("Remember me", german);
    }

    [Fact]
    public void Chinese_announcements_use_a_full_width_separator()
    {
        // An ASCII comma reads without a pause in a Chinese voice; the full-width form is
        // what gives "名称，按钮" its natural cadence.
        var text = WindowAnnouncementFormatter.Format(WindowAnnouncementKind.Opened, "记事本", Chinese);

        Assert.Equal("新窗口，记事本", text);
    }

    [Fact]
    public void Window_announcements_are_translated_but_the_window_title_is_not()
    {
        var text = WindowAnnouncementFormatter.Format(WindowAnnouncementKind.Closed, "Notepad", German);

        Assert.StartsWith("Fenster geschlossen", text);
        Assert.EndsWith("Notepad", text);
    }

    [Fact]
    public void A_missing_translation_falls_back_to_english_rather_than_blank()
    {
        // French has no .resx - every key should still resolve, via the neutral resource.
        var french = CultureInfo.GetCultureInfo("fr-FR");

        var text = SpeechStrings.Get("Speech_State_Checked", french);

        Assert.Equal("checked", text);
    }
}
