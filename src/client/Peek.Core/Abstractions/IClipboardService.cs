namespace Peek.Core.Abstractions;

public interface IClipboardService
{
    /// <summary>
    /// Returns the current clipboard text, or null if the clipboard
    /// does not contain text or is unavailable.
    /// </summary>
    Task<string?> GetTextAsync();

    /// <summary>
    /// Writes <paramref name="text"/> to the clipboard.
    /// Pass null to clear the clipboard.
    /// </summary>
    Task SetTextAsync(string? text);

    Task ClearAsync();
    Task<IEnumerable<string>?> GetDataFormatsIdAsync();
}