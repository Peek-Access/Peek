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

    /// <summary>
    /// Optional read-only folder, installed alongside the worker executable (not
    /// DataDirectory), holding a pre-packaged Piper runtime plus the three voice models Peek
    /// ships UI translations for - built by Peek.Worker.Tts.AssetBundler at publish time (see
    /// pack/peek.iss and the pack-*/release GitHub Actions workflows), not checked into the
    /// repo itself. When present, PiperTtsService copies from here into DataDirectory once
    /// instead of downloading, so a fresh install's first announcement never waits on a
    /// network fetch for a voice Peek already shipped. Null (the default here) on a dev build
    /// or any install that predates this folder existing - everything falls back to the
    /// original download-on-demand behavior exactly as before, just slower on first use.
    /// </summary>
    public string? BundledAssetsDirectory { get; set; } =
        Directory.Exists(Path.Combine(AppContext.BaseDirectory, "tts-assets"))
            ? Path.Combine(AppContext.BaseDirectory, "tts-assets")
            : null;

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
