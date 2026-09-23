using PiperSharp;

// Build/release-time tool (see pack/peek.iss's [Files] section and the pack-*/release
// workflows) - fetches the Piper runtime plus the three voice models Peek ships UI
// translations for (see SpeechSettings.VoiceIdByLanguage's own defaults) into a folder that
// gets published alongside Peek.Worker.exe, so a fresh install never needs a first-run
// network download just to hear its own default voices. Never run at Peek's own runtime -
// PiperTtsService/PiperTtsOptions.BundledAssetsDirectory is what reads this tool's output;
// everything else Peek ever downloads (a voice a user picks that isn't one of these three)
// still goes through the normal on-demand path, unaffected by this.
//
// Deliberately calls the exact same PiperDownloader API PiperTtsService itself calls at
// runtime, rather than hand-rolling the download URLs here - whatever platform/version/format
// logic PiperSharp uses stays in exactly one place, so this can never quietly drift out of
// sync with what a real install would have fetched on its own.
if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("Usage: Peek.Worker.Tts.AssetBundler <output-directory>");
    return 1;
}

var outputDir = args[0];
Directory.CreateDirectory(outputDir);

var modelsDir = Path.Combine(outputDir, "models");
Directory.CreateDirectory(modelsDir);

var voiceIds = new[] { "en_US-lessac-medium", "de_DE-thorsten-medium", "zh_CN-huayan-medium" };

try
{
    Console.WriteLine($"Downloading Piper runtime into {outputDir} ...");
    // Extracts into <outputDir>/piper/... - takes the parent directory, not the "piper"
    // subfolder itself, exactly like PiperTtsService.EnsurePiperInstalledAsync does (the
    // release archive already contains its own top-level "piper/" folder).
    await PiperDownloader.DownloadPiper().ExtractPiper(outputDir);
    Console.WriteLine("Piper runtime ready.");

    foreach (var voiceId in voiceIds)
    {
        Console.WriteLine($"Downloading voice model '{voiceId}' ...");
        var descriptor = await PiperDownloader.GetModelByKey(voiceId)
            ?? throw new InvalidOperationException($"Unknown Piper voice model key '{voiceId}'.");
        await descriptor.DownloadModel(modelsDir);
        Console.WriteLine($"Voice model '{voiceId}' ready.");
    }
}
catch (Exception ex)
{
    // Fails the build loudly rather than shipping an installer with a half-populated bundle -
    // a partially-downloaded piper.exe with no models (or vice versa) would be worse than no
    // bundle at all, since PiperTtsService/PiperTtsOptions only checks for presence, not
    // completeness.
    Console.Error.WriteLine($"Bundling TTS assets failed: {ex}");
    return 1;
}

Console.WriteLine("All TTS assets bundled successfully.");
return 0;
