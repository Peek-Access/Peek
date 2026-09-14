namespace Peek.Core.Settings;

/// <summary>
/// Loads, persists, and exposes change notifications for <see cref="PeekSettings"/>.
/// </summary>
public interface ISettingsService
{
    /// <summary>The current, in-memory settings. Never null after <see cref="LoadAsync"/> has run once.</summary>
    PeekSettings Current { get; }

    /// <summary>Emits the current settings immediately on subscription, then again after every successful save.</summary>
    IObservable<PeekSettings> Changes { get; }

    /// <summary>
    /// Synchronous load, safe to call before the UI dispatcher loop is pumping
    /// (e.g. during Avalonia startup) - unlike blocking on <see cref="LoadAsync"/>
    /// there, which can deadlock waiting for a continuation the not-yet-running
    /// dispatcher would otherwise service.
    /// </summary>
    void Load();

    Task LoadAsync(CancellationToken ct = default);

    Task SaveAsync(CancellationToken ct = default);

    /// <summary>Mutates <see cref="Current"/> in place, validates, persists, and notifies <see cref="Changes"/> - the normal way UI code changes a setting.</summary>
    Task UpdateAsync(Action<PeekSettings> mutate, CancellationToken ct = default);
}
