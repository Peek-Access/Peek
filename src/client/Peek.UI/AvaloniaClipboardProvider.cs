using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Peek.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Peek.UI;

internal class AvaloniaClipboardProvider : IClipboardService
{
    private readonly Lazy<IClipboard?> _lazyClipboard;
    public AvaloniaClipboardProvider(IServiceProvider serviceProvider) 
    {
        _lazyClipboard = new Lazy<IClipboard?>(() =>
        {
            var topLevel = TopLevel.GetTopLevel(
                (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow
            );
            return topLevel?.Clipboard;
        });
    }

    public async Task ClearAsync()
    {
        if (_lazyClipboard.Value is null)
        {
            throw new InvalidOperationException("IClipboard has not been initialized!");
        }
        await Application.Current!.Dispatcher.InvokeAsync(_lazyClipboard.Value.ClearAsync);
    }

    public async Task<string?> GetTextAsync()
    {
        if (_lazyClipboard.Value is null)
        {
            throw new InvalidOperationException("IClipboard has not been initialized!");
        }
        return await Application.Current!.Dispatcher.InvokeAsync(async () =>  await _lazyClipboard.Value.TryGetTextAsync());
    }

    public async Task SetTextAsync(string? text)
    {
        if (_lazyClipboard.Value is null)
        {
            throw new InvalidOperationException("IClipboard has not been initialized!");
        }
        await Application.Current!.Dispatcher.InvokeAsync(async ()=> await _lazyClipboard.Value.SetTextAsync(text));
    }

    public async Task<IEnumerable<string>?> GetDataFormatsIdAsync()
    {
        if (_lazyClipboard.Value is null)
        {
            throw new InvalidOperationException("IClipboard has not been initialized!");
        }
        var formats = await _lazyClipboard.Value.GetDataFormatsAsync();
        return formats?.Select(f => f.Identifier);
    }
}
