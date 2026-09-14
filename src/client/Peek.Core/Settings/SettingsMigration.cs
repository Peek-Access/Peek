namespace Peek.Core.Settings;

/// <summary>
/// Upgrades an older on-disk settings shape to <see cref="PeekSettings.CurrentVersion"/>.
/// No migrations exist yet (version 1 is the only version that has ever shipped) -
/// this is the extension point for when <see cref="PeekSettings.CurrentVersion"/>
/// needs to become 2: add a case here rather than changing field meanings in place.
/// </summary>
internal static class SettingsMigration
{
    public static PeekSettings Apply(PeekSettings loaded)
    {
        // switch (loaded.Version) { case 1 when PeekSettings.CurrentVersion > 1: ... }
        loaded.Version = PeekSettings.CurrentVersion;
        return loaded;
    }
}
