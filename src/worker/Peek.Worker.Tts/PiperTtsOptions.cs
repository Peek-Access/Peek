namespace Peek.Worker.Tts;

public sealed class PiperTtsOptions
{
    /// <summary>
    /// Root folder for the downloaded Piper runtime + voice models. Deliberately
    /// %LOCALAPPDATA%\Peek\tts (matching where JsonSettingsService already keeps
    /// settings.json), not next to the worker's own executable: an installed app is
    /// commonly deployed under Program Files, which a normal (non-admin) process cannot
    /// create new subfolders under - PiperTtsService's constructor creates this
    /// directory unconditionally, and since it's an eager dependency of
    /// RpcRequestDispatcher (in turn a dependency of the always-started
    /// WorkerPipeServer), a failure here previously crashed the entire worker process
    /// before it ever opened its pipe - not just disabled TTS.
    /// </summary>
    public string DataDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Peek", "tts");

    /// <summary>Piper voice model key (from Hugging Face's rhasspy/piper-voices) used when a request does not specify one.</summary>
    public string DefaultVoiceId { get; set; } = "en_US-lessac-medium";

    /// <summary>
    /// When true, the Piper executable and voice models are fetched automatically on
    /// first use if missing locally. This is the one place "local-first" (§16) means
    /// "local after a one-time network fetch" rather than fully offline; set false to
    /// require pre-staged assets under <see cref="DataDirectory"/> instead.
    /// </summary>
    public bool AutoDownloadAssets { get; set; } = true;

    public bool UseCuda { get; set; }
}
