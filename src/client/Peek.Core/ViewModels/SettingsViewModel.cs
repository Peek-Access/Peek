using Irihi.Lingua;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Peek.Worker.Contracts.Llm;
using Peek.Core.Abstractions;
using Peek.Core.Models;
using Peek.Core.Services;
using Peek.Core.Services.Llm;
using Peek.Core.Services.Ocr;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using Peek.Ipc.Worker;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using System.Collections.ObjectModel;
using System.Globalization;
using ReactiveUI.Primitives;

namespace Peek.Core.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ILinguaManager _linguaManager;
    private readonly ISettingsService _settingsService;
    private readonly WorkerConnection _workerConnection;
    private readonly IAccessibilitySpeechService _speechService;
    private readonly AudioPlayer _audioPlayer;
    private readonly ILogger<SettingsViewModel> _logger;

    /// <summary>The same DI-singleton instance App.axaml.cs resolves at startup to restore
    /// the persisted color before any window is shown - injecting it here (rather than a
    /// new instance) means the picker UI, now living in Settings instead of the title bar,
    /// and that startup restore share one source of truth.</summary>
    public ColorPickerViewModel ColorPicker { get; }

    [Reactive]
    private LanguageItem _currentCulture;
    [Reactive]
    private LanguageItem _speechLanguage;
    [Reactive]
    private SpeechVerbosity _verbosity;
    [Reactive]
    private float _speechRate;
    [Reactive]
    private string _englishVoiceId = "";
    [Reactive]
    private string _germanVoiceId = "";
    [Reactive]
    private string _chineseVoiceId = "";
    [Reactive]
    private OcrUserPreference _ocrPreference;
    [Reactive]
    private double _ocrScreenChangeThreshold;
    [Reactive]
    private bool _aiEnabled;
    [Reactive]
    private string _aiProvider = "ollama";
    [Reactive]
    private string _aiEndpoint = "";
    [Reactive]
    private string? _aiApiKey;
    [Reactive]
    private string _aiModel = "";
    [Reactive]
    private bool _isTestingAiConnection;
    [Reactive]
    private string _aiConnectionTestResult = "";
    [Reactive]
    private bool _announcementHistoryEnabled;
    [Reactive]
    private int _announcementHistoryMaxCharacters;
    [Reactive]
    private bool _isPreviewing;
    [Reactive]
    private bool _windowAnnouncementEnabled;
    [Reactive]
    private bool _announceWindowOpened;
    [Reactive]
    private bool _announceWindowClosed;
    [Reactive]
    private bool _announceFocusChanged;
    [Reactive]
    private WindowAnnouncementSubject _windowAnnouncementSubject;
    [Reactive]
    private ShellMode _shellMode;
    [Reactive]
    private DockEdge _dockEdge;
    [Reactive]
    private double _dockWidth;
    [Reactive]
    private bool _pageSwitchSoundEnabled;
    [Reactive]
    private string _inspectorExpandShortcut = "";
    [Reactive]
    private string _dockMonitorNextShortcut = "";
    [Reactive]
    private string _dockMonitorPreviousShortcut = "";
    [Reactive]
    private string _appMonitorLaunchShortcut = "";
    [Reactive]
    private string _processMonitorKillShortcut = "";
    [Reactive]
    private string _processMonitorOpenFolderShortcut = "";
    [Reactive]
    private string _describeFocusedElementShortcut = "";
    [Reactive]
    private string _toggleTrackingShortcut = "";
    [Reactive]
    private bool _showSystemProcesses;
    [Reactive]
    private bool _announceDetailedProcessInfo;
    [Reactive]
    private bool _announceOnHover;
    [Reactive]
    private bool _announceOnFocus;
    [Reactive]
    private bool _highlightFocusedElement;
    [Reactive]
    private bool _announceOwnInterface;
    [Reactive]
    private string _stopSpeakingShortcut = "";

    public IObservable<string?> DisplayLanguage => _linguaManager.GetObservable("Settings_Language");

    public ObservableCollection<LanguageItem> Languages { get; } =
    [
        new("English", new CultureInfo("en-US")),
        new("中文", new CultureInfo("zh-CN")),
        new("Deutsch", new CultureInfo("de-DE")),
    ];

    public SpeechVerbosity[] VerbosityOptions { get; } = Enum.GetValues<SpeechVerbosity>();

    public OcrUserPreference[] OcrPreferenceOptions { get; } = Enum.GetValues<OcrUserPreference>();

    public WindowAnnouncementSubject[] WindowAnnouncementSubjectOptions { get; } = Enum.GetValues<WindowAnnouncementSubject>();

    public ShellMode[] ShellModeOptions { get; } = Enum.GetValues<ShellMode>();

    public DockEdge[] DockEdgeOptions { get; } = Enum.GetValues<DockEdge>();

    public string[] AiProviderOptions { get; } = ["ollama", "openai", "anthropic", "gemini", "openrouter", "custom"];

    public SettingsViewModel(IServiceProvider serviceProvider, ILogger<SettingsViewModel> logger)
    {
        _linguaManager = serviceProvider.GetRequiredService<ILinguaManager>();
        _settingsService = serviceProvider.GetRequiredService<ISettingsService>();
        _workerConnection = serviceProvider.GetRequiredService<WorkerConnection>();
        _speechService = serviceProvider.GetRequiredService<IAccessibilitySpeechService>();
        _audioPlayer = serviceProvider.GetRequiredService<AudioPlayer>();
        ColorPicker = serviceProvider.GetRequiredService<ColorPickerViewModel>();
        _logger = logger;

        var settings = _settingsService.Current;

        _currentCulture = Languages.FirstOrDefault(l => l.Culture.Name == settings.Localization.UiLanguage)
            ?? Languages.First();
        _speechLanguage = Languages.FirstOrDefault(l => l.Culture.Name == (settings.Localization.TtsLanguage ?? settings.Localization.UiLanguage))
            ?? Languages.First();
        _verbosity = settings.Speech.Verbosity;
        _speechRate = settings.Speech.Rate;
        _englishVoiceId = settings.Speech.VoiceIdByLanguage.GetValueOrDefault("en", "en_US-lessac-medium");
        _germanVoiceId = settings.Speech.VoiceIdByLanguage.GetValueOrDefault("de", "de_DE-thorsten-medium");
        _chineseVoiceId = settings.Speech.VoiceIdByLanguage.GetValueOrDefault("zh", "zh_CN-huayan-medium");
        _ocrPreference = settings.Ocr.Preference;
        _ocrScreenChangeThreshold = settings.Ocr.ScreenChangeThreshold;
        _aiEnabled = settings.Ai.Enabled;
        _aiProvider = settings.Ai.Provider;
        var activeAiCredentials = settings.Ai.Providers.GetValueOrDefault(_aiProvider) ?? new LlmProviderCredentials();
        _aiEndpoint = activeAiCredentials.Endpoint;
        _aiApiKey = activeAiCredentials.ApiKey;
        _aiModel = activeAiCredentials.Model;
        _announcementHistoryEnabled = settings.AnnouncementHistory.Enabled;
        _announcementHistoryMaxCharacters = settings.AnnouncementHistory.MaxCharacters;
        _windowAnnouncementEnabled = settings.WindowAnnouncement.Enabled;
        _announceWindowOpened = settings.WindowAnnouncement.AnnounceWindowOpened;
        _announceWindowClosed = settings.WindowAnnouncement.AnnounceWindowClosed;
        _announceFocusChanged = settings.WindowAnnouncement.AnnounceFocusChanged;
        _windowAnnouncementSubject = settings.WindowAnnouncement.Subject;
        _shellMode = settings.DockShell.Mode;
        _dockEdge = settings.DockShell.DockEdge;
        _dockWidth = settings.DockShell.DockWidth;
        _pageSwitchSoundEnabled = settings.DockShell.PageSwitchSoundEnabled;
        _inspectorExpandShortcut = settings.Keyboard.Shortcuts.GetValueOrDefault("InspectorToggleExpand", "Space");
        _dockMonitorNextShortcut = settings.Keyboard.Shortcuts.GetValueOrDefault("DockMonitorNext", "PageDown");
        _dockMonitorPreviousShortcut = settings.Keyboard.Shortcuts.GetValueOrDefault("DockMonitorPrevious", "PageUp");
        _appMonitorLaunchShortcut = settings.Keyboard.Shortcuts.GetValueOrDefault("AppMonitorLaunch", "Enter");
        _processMonitorKillShortcut = settings.Keyboard.Shortcuts.GetValueOrDefault("ProcessMonitorKill", "Delete");
        _processMonitorOpenFolderShortcut = settings.Keyboard.Shortcuts.GetValueOrDefault("ProcessMonitorOpenFolder", "Ctrl+E");
        _describeFocusedElementShortcut = settings.Keyboard.Shortcuts.GetValueOrDefault("DescribeFocusedElement", "Ctrl+Alt+D");
        _toggleTrackingShortcut = settings.Keyboard.Shortcuts.GetValueOrDefault("ToggleTracking", "Ctrl+Alt+T");
        _showSystemProcesses = settings.ProcessMonitor.ShowSystemProcesses;
        _announceDetailedProcessInfo = settings.ProcessMonitor.AnnounceDetailedInfo;
        _announceOnHover = settings.Accessibility.AnnounceOnHover;
        _announceOnFocus = settings.Accessibility.AnnounceOnFocus;
        _highlightFocusedElement = settings.Accessibility.HighlightFocusedElement;
        _announceOwnInterface = settings.Accessibility.AnnounceOwnInterface;
        _stopSpeakingShortcut = settings.Keyboard.Shortcuts.GetValueOrDefault("StopSpeaking", "Ctrl+Alt+S");

        this.WhenAnyValue(x => x.CurrentCulture)
            .Where(culture => culture is not null)
            .Subscribe(culture =>
            {
                _linguaManager.UpdateCulture(culture.Culture);
                _ = _settingsService.UpdateAsync(s => s.Localization.UiLanguage = culture.Culture.Name);
            });

        this.WhenAnyValue(x => x.SpeechLanguage)
            .Where(l => l is not null)
            .Skip(1)
            .Subscribe(l => _ = _settingsService.UpdateAsync(s => s.Localization.TtsLanguage = l!.Culture.Name));
        this.WhenAnyValue(x => x.Verbosity).Skip(1)
            .Subscribe(v => _ = _settingsService.UpdateAsync(s => s.Speech.Verbosity = v));
        this.WhenAnyValue(x => x.SpeechRate).Skip(1)
            .Subscribe(r => _ = _settingsService.UpdateAsync(s => s.Speech.Rate = r));
        this.WhenAnyValue(x => x.EnglishVoiceId).Skip(1)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Subscribe(v => _ = _settingsService.UpdateAsync(s => s.Speech.VoiceIdByLanguage["en"] = v));
        this.WhenAnyValue(x => x.GermanVoiceId).Skip(1)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Subscribe(v => _ = _settingsService.UpdateAsync(s => s.Speech.VoiceIdByLanguage["de"] = v));
        this.WhenAnyValue(x => x.ChineseVoiceId).Skip(1)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Subscribe(v => _ = _settingsService.UpdateAsync(s => s.Speech.VoiceIdByLanguage["zh"] = v));
        this.WhenAnyValue(x => x.OcrPreference).Skip(1)
            .Subscribe(p => _ = _settingsService.UpdateAsync(s => s.Ocr.Preference = p));
        this.WhenAnyValue(x => x.OcrScreenChangeThreshold).Skip(1)
            .Subscribe(v => _ = _settingsService.UpdateAsync(s => s.Ocr.ScreenChangeThreshold = v));
        this.WhenAnyValue(x => x.AiEnabled).Skip(1)
            .Subscribe(e => _ = _settingsService.UpdateAsync(s => s.Ai.Enabled = e));
        this.WhenAnyValue(x => x.AiProvider).Skip(1)
            .Subscribe(providerId =>
            {
                _ = _settingsService.UpdateAsync(s => s.Ai.Provider = providerId);

                // Switching the active provider swaps the three credential fields below to
                // that provider's own stored values - each field's own subscription then
                // (harmlessly) re-persists the same value it just loaded, same pattern as
                // CurrentCulture's load-time re-apply above.
                var credentials = _settingsService.Current.Ai.Providers.GetValueOrDefault(providerId) ?? new LlmProviderCredentials();
                AiEndpoint = credentials.Endpoint;
                AiApiKey = credentials.ApiKey;
                AiModel = credentials.Model;
                AiConnectionTestResult = "";
            });
        this.WhenAnyValue(x => x.AiEndpoint).Skip(1)
            .Subscribe(v => _ = _settingsService.UpdateAsync(s => GetOrAddAiProviderCredentials(s, AiProvider).Endpoint = v));
        this.WhenAnyValue(x => x.AiApiKey).Skip(1)
            .Subscribe(v => _ = _settingsService.UpdateAsync(s => GetOrAddAiProviderCredentials(s, AiProvider).ApiKey = v));
        this.WhenAnyValue(x => x.AiModel).Skip(1)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Subscribe(m => _ = _settingsService.UpdateAsync(s => GetOrAddAiProviderCredentials(s, AiProvider).Model = m));
        this.WhenAnyValue(x => x.AnnouncementHistoryEnabled).Skip(1)
            .Subscribe(v => _ = _settingsService.UpdateAsync(s => s.AnnouncementHistory.Enabled = v));
        this.WhenAnyValue(x => x.AnnouncementHistoryMaxCharacters).Skip(1)
            .Subscribe(v => _ = _settingsService.UpdateAsync(s => s.AnnouncementHistory.MaxCharacters = v));
        this.WhenAnyValue(x => x.WindowAnnouncementEnabled).Skip(1)
            .Subscribe(e => _ = _settingsService.UpdateAsync(s => s.WindowAnnouncement.Enabled = e));
        this.WhenAnyValue(x => x.AnnounceWindowOpened).Skip(1)
            .Subscribe(e => _ = _settingsService.UpdateAsync(s => s.WindowAnnouncement.AnnounceWindowOpened = e));
        this.WhenAnyValue(x => x.AnnounceWindowClosed).Skip(1)
            .Subscribe(e => _ = _settingsService.UpdateAsync(s => s.WindowAnnouncement.AnnounceWindowClosed = e));
        this.WhenAnyValue(x => x.AnnounceFocusChanged).Skip(1)
            .Subscribe(e => _ = _settingsService.UpdateAsync(s => s.WindowAnnouncement.AnnounceFocusChanged = e));
        this.WhenAnyValue(x => x.WindowAnnouncementSubject).Skip(1)
            .Subscribe(subj => _ = _settingsService.UpdateAsync(s => s.WindowAnnouncement.Subject = subj));
        this.WhenAnyValue(x => x.ShellMode).Skip(1)
            .Subscribe(mode => _ = _settingsService.UpdateAsync(s => s.DockShell.Mode = mode));
        this.WhenAnyValue(x => x.DockEdge).Skip(1)
            .Subscribe(edge => _ = _settingsService.UpdateAsync(s => s.DockShell.DockEdge = edge));
        this.WhenAnyValue(x => x.DockWidth).Skip(1)
            .Subscribe(width => _ = _settingsService.UpdateAsync(s => s.DockShell.DockWidth = width));
        this.WhenAnyValue(x => x.PageSwitchSoundEnabled).Skip(1)
            .Subscribe(v => _ = _settingsService.UpdateAsync(s => s.DockShell.PageSwitchSoundEnabled = v));
        this.WhenAnyValue(x => x.InspectorExpandShortcut).Skip(1)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Subscribe(g => _ = _settingsService.UpdateAsync(s => s.Keyboard.Shortcuts["InspectorToggleExpand"] = g));
        this.WhenAnyValue(x => x.DockMonitorNextShortcut).Skip(1)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Subscribe(g => _ = _settingsService.UpdateAsync(s => s.Keyboard.Shortcuts["DockMonitorNext"] = g));
        this.WhenAnyValue(x => x.DockMonitorPreviousShortcut).Skip(1)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Subscribe(g => _ = _settingsService.UpdateAsync(s => s.Keyboard.Shortcuts["DockMonitorPrevious"] = g));
        this.WhenAnyValue(x => x.AppMonitorLaunchShortcut).Skip(1)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Subscribe(g => _ = _settingsService.UpdateAsync(s => s.Keyboard.Shortcuts["AppMonitorLaunch"] = g));
        this.WhenAnyValue(x => x.ProcessMonitorKillShortcut).Skip(1)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Subscribe(g => _ = _settingsService.UpdateAsync(s => s.Keyboard.Shortcuts["ProcessMonitorKill"] = g));
        this.WhenAnyValue(x => x.ProcessMonitorOpenFolderShortcut).Skip(1)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Subscribe(g => _ = _settingsService.UpdateAsync(s => s.Keyboard.Shortcuts["ProcessMonitorOpenFolder"] = g));
        this.WhenAnyValue(x => x.DescribeFocusedElementShortcut).Skip(1)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Subscribe(g => _ = _settingsService.UpdateAsync(s => s.Keyboard.Shortcuts["DescribeFocusedElement"] = g));
        this.WhenAnyValue(x => x.ToggleTrackingShortcut).Skip(1)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Subscribe(g => _ = _settingsService.UpdateAsync(s => s.Keyboard.Shortcuts["ToggleTracking"] = g));
        this.WhenAnyValue(x => x.ShowSystemProcesses).Skip(1)
            .Subscribe(v => _ = _settingsService.UpdateAsync(s => s.ProcessMonitor.ShowSystemProcesses = v));
        this.WhenAnyValue(x => x.AnnounceDetailedProcessInfo).Skip(1)
            .Subscribe(v => _ = _settingsService.UpdateAsync(s => s.ProcessMonitor.AnnounceDetailedInfo = v));
        this.WhenAnyValue(x => x.AnnounceOnHover).Skip(1)
            .Subscribe(v => _ = _settingsService.UpdateAsync(s => s.Accessibility.AnnounceOnHover = v));
        this.WhenAnyValue(x => x.AnnounceOnFocus).Skip(1)
            .Subscribe(v => _ = _settingsService.UpdateAsync(s => s.Accessibility.AnnounceOnFocus = v));
        this.WhenAnyValue(x => x.HighlightFocusedElement).Skip(1)
            .Subscribe(v => _ = _settingsService.UpdateAsync(s => s.Accessibility.HighlightFocusedElement = v));
        this.WhenAnyValue(x => x.AnnounceOwnInterface).Skip(1)
            .Subscribe(v => _ = _settingsService.UpdateAsync(s => s.Accessibility.AnnounceOwnInterface = v));
        this.WhenAnyValue(x => x.StopSpeakingShortcut).Skip(1)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Subscribe(g => _ = _settingsService.UpdateAsync(s => s.Keyboard.Shortcuts["StopSpeaking"] = g));
    }

    /// <summary>Previews the full pipeline (language detection + primary-language voice + rate) end to end, the way an actual announcement is spoken.</summary>
    [ReactiveCommand]
    public async Task PreviewVoice()
    {
        if (IsPreviewing) return;

        IsPreviewing = true;
        try
        {
            await _speechService.AnnounceTextAsync("This is a preview of the current voice and speed.", SpeechPriority.UserRequested);
        }
        finally
        {
            IsPreviewing = false;
        }
    }

    [ReactiveCommand]
    public Task PreviewEnglishVoice() => PreviewVoiceForLanguageAsync("en", EnglishVoiceId, "This is a preview of the English voice.");

    [ReactiveCommand]
    public Task PreviewGermanVoice() => PreviewVoiceForLanguageAsync("de", GermanVoiceId, "Dies ist eine Vorschau der deutschen Stimme.");

    [ReactiveCommand]
    public Task PreviewChineseVoice() => PreviewVoiceForLanguageAsync("zh", ChineseVoiceId, "这是普通话语音的预览。");

    /// <summary>
    /// Speaks <paramref name="sampleText"/> with <paramref name="voiceId"/> specifically,
    /// bypassing AccessibilitySpeechService's language detection - useful here because the
    /// point is to hear the exact voice the user is currently editing, regardless of what
    /// their primary speech language is set to.
    /// </summary>
    private async Task PreviewVoiceForLanguageAsync(string languageCode, string voiceId, string sampleText)
    {
        if (IsPreviewing || string.IsNullOrWhiteSpace(voiceId)) return;

        IsPreviewing = true;
        try
        {
            var result = await _workerConnection.Client.Tts.SpeakAsync(sampleText, voiceId, SpeechRate);
            _audioPlayer.Stop();
            await _audioPlayer.PlayBytesAsync(result.AudioData, result.Format);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Voice preview failed for language '{Language}' (voice '{VoiceId}')", languageCode, voiceId);
        }
        finally
        {
            IsPreviewing = false;
        }
    }

    /// <summary>Checks whether the currently-configured provider (endpoint/key/model, as currently shown in the AI section) is actually reachable - lets the user catch a typo'd endpoint or bad key before relying on it during real use.</summary>
    [ReactiveCommand]
    public async Task TestAiConnection()
    {
        if (IsTestingAiConnection) return;

        IsTestingAiConnection = true;
        AiConnectionTestResult = "";
        try
        {
            var provider = LlmProviderConfigResolver.Resolve(_settingsService.Current);
            var status = await _workerConnection.Client.Llm.GetStatusAsync(provider);
            AiConnectionTestResult = status.IsReady
                ? $"✓ Connected ({status.ProviderName})"
                : "✗ Not reachable";
        }
        catch (Exception ex)
        {
            AiConnectionTestResult = $"✗ {ex.Message}";
        }
        finally
        {
            IsTestingAiConnection = false;
        }
    }

    private static LlmProviderCredentials GetOrAddAiProviderCredentials(PeekSettings settings, string providerId)
    {
        if (!settings.Ai.Providers.TryGetValue(providerId, out var credentials))
        {
            credentials = new LlmProviderCredentials();
            settings.Ai.Providers[providerId] = credentials;
        }
        return credentials;
    }
}
