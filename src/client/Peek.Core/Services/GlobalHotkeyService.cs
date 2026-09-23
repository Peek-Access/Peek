using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Peek.Core.Abstractions;
using Peek.Core.Services.Llm;
using Peek.Core.Services.Speech;
using Peek.Core.Settings;
using ReactiveUI.Primitives;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Peek.Core.Services;

/// <summary>
/// Consumes the two global (system-wide, works-while-unfocused) shortcuts declared in
/// <see cref="KeyboardSettings.Shortcuts"/> - "DescribeFocusedElement" and
/// "ToggleTracking" - via a WH_KEYBOARD_LL hook (the same <see cref="WindowsHookService"/>
/// <see cref="WindowsMouseTracker"/> already uses for WH_MOUSE_LL), so they fire no matter
/// which application currently has focus. Every other shortcut in KeyboardSettings is
/// local (a KeyBinding/KeyDown handler owned by the relevant view/window) and needs
/// nothing here.
/// </summary>
[SupportedOSPlatform("windows6.0")]
public sealed class GlobalHotkeyService : IDisposable
{
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_SYSKEYDOWN = 0x0104;

    private readonly WindowsHookService _hookService;
    private readonly ElementTracker _elementTracker;
    private readonly IElementDescriptionService _describer;
    private readonly IAccessibilitySpeechService _speechService;
    private readonly IPeekSelfWindow _selfWindow;
    private readonly ILogger<GlobalHotkeyService> _logger;
    private readonly IDisposable _settingsSubscription;
    private int _disposed;

    private HotkeyGesture _describeGesture;
    private HotkeyGesture _toggleTrackingGesture;
    private HotkeyGesture _stopSpeakingGesture;
    private HotkeyGesture _showPeekGesture;

    public GlobalHotkeyService(
        WindowsHookService hookService,
        ElementTracker elementTracker,
        IElementDescriptionService describer,
        IAccessibilitySpeechService speechService,
        IPeekSelfWindow selfWindow,
        ISettingsService settingsService,
        ILogger<GlobalHotkeyService> logger)
    {
        _hookService = hookService;
        _elementTracker = elementTracker;
        _describer = describer;
        _speechService = speechService;
        _selfWindow = selfWindow;
        _logger = logger;

        ApplyShortcuts(settingsService.Current.Keyboard);
        _settingsSubscription = settingsService.Changes.Subscribe(s => ApplyShortcuts(s.Keyboard));

        _hookService.RegisterLowLevelHook(WINDOWS_HOOK_ID.WH_KEYBOARD_LL);
        _hookService.HookFired += OnHookFired;
    }

    private void ApplyShortcuts(KeyboardSettings keyboard)
    {
        _describeGesture = ParseOrFallback(
            keyboard.Shortcuts.GetValueOrDefault("DescribeFocusedElement", "Ctrl+Alt+D"), "Ctrl+Alt+D");
        _toggleTrackingGesture = ParseOrFallback(
            keyboard.Shortcuts.GetValueOrDefault("ToggleTracking", "Ctrl+Alt+T"), "Ctrl+Alt+T");
        // Not bare Ctrl, which is what NVDA/JAWS use: they can get away with it because
        // they sit in the input path and can swallow the key. This is a passive low-level
        // hook that never consumes anything (see WindowsHookService.LowLevelCallback, which
        // always calls CallNextHookEx), so binding bare Ctrl would fire on every
        // copy/paste/shortcut the user presses in any app.
        _stopSpeakingGesture = ParseOrFallback(
            keyboard.Shortcuts.GetValueOrDefault("StopSpeaking", "Ctrl+Alt+S"), "Ctrl+Alt+S");
        _showPeekGesture = ParseOrFallback(
            keyboard.Shortcuts.GetValueOrDefault("ShowPeek", "Ctrl+Alt+P"), "Ctrl+Alt+P");
    }

    private HotkeyGesture ParseOrFallback(string gesture, string fallback)
    {
        try
        {
            return HotkeyGesture.Parse(gesture);
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Could not parse global hotkey gesture '{Gesture}' - using default '{Fallback}'",
                gesture, fallback);
            return HotkeyGesture.Parse(fallback);
        }
    }

    private void OnHookFired(object? sender, WindowsHookEventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;

        if (e.HookId != WINDOWS_HOOK_ID.WH_KEYBOARD_LL)
            return;

        if (e.WParam != WM_KEYDOWN && e.WParam != WM_SYSKEYDOWN)
            return;

        var kb = Marshal.PtrToStructure<KbdLlHookStruct>(e.LParam);
        var vkCode = (int)kb.VkCode;

        if (_describeGesture.Matches(vkCode))
        {
            _ = RunDescribeAsync();
        }
        else if (_toggleTrackingGesture.Matches(vkCode))
        {
            _elementTracker.IsTracking = !_elementTracker.IsTracking;
        }
        else if (_stopSpeakingGesture.Matches(vkCode))
        {
            _ = RunStopSpeakingAsync();
        }
        else if (_showPeekGesture.Matches(vkCode))
        {
            _selfWindow.ShowAndActivate();
        }
    }

    private async Task RunStopSpeakingAsync()
    {
        try
        {
            await _speechService.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Global hotkey 'StopSpeaking' failed");
        }
    }

    private async Task RunDescribeAsync()
    {
        var element = _elementTracker.CurrentElement;
        if (element is null) return;

        try
        {
            await _describer.DescribeAsync(element);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Global hotkey 'DescribeFocusedElement' failed");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _hookService.HookFired -= OnHookFired;
        _settingsSubscription.Dispose();
    }

    /// <summary>Mirrors the native KBDLLHOOKSTRUCT layout (WinUser.h) - defined by hand
    /// rather than via CsWin32 since it's a small, stable, decades-old ABI and every other
    /// low-level-hook payload type in this codebase (<see cref="WindowsHookEventArgs"/>,
    /// <see cref="WinEventArgs"/>) is already a hand-written type, not generated.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint DwExtraInfo;
    }

    /// <summary>
    /// A parsed "Ctrl+Alt+D"-style gesture string, checked against a WH_KEYBOARD_LL vkCode
    /// plus live modifier state (<see cref="PInvoke.GetAsyncKeyState"/>) - independent of
    /// Avalonia's own <c>KeyGesture</c>, which only matches routed, focus-scoped
    /// <c>KeyEventArgs</c> and has no way to observe a key event outside this process.
    /// </summary>
    private readonly record struct HotkeyGesture(int VkCode, bool Ctrl, bool Alt, bool Shift, bool Win)
    {
        // Standard, WinUser.h-documented virtual-key codes for the modifier keys - not
        // sourced from CsWin32 for the same reason as KbdLlHookStruct above.
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12; // Alt
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        public static HotkeyGesture Parse(string gesture)
        {
            var parts = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                throw new FormatException($"Empty hotkey gesture '{gesture}'.");

            bool ctrl = false, alt = false, shift = false, win = false;
            string? keyToken = null;

            foreach (var part in parts)
            {
                switch (part.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        ctrl = true;
                        break;
                    case "alt":
                        alt = true;
                        break;
                    case "shift":
                        shift = true;
                        break;
                    case "win":
                    case "windows":
                        win = true;
                        break;
                    default:
                        if (keyToken is not null)
                            throw new FormatException($"Hotkey gesture '{gesture}' has more than one non-modifier key.");
                        keyToken = part;
                        break;
                }
            }

            if (keyToken is null)
                throw new FormatException($"Hotkey gesture '{gesture}' has no non-modifier key.");

            return new HotkeyGesture(ResolveVirtualKey(gesture, keyToken), ctrl, alt, shift, win);
        }

        // Letter/digit virtual-key codes are, by Win32 convention (WinUser.h), literally
        // their uppercase ASCII values (VK_A == 'A' == 0x41, VK_0 == '0' == 0x30) - not
        // named constants in the header, so there's nothing to look up for those.
        private static int ResolveVirtualKey(string gesture, string token)
        {
            if (token.Length == 1)
            {
                var c = char.ToUpperInvariant(token[0]);
                if (c is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
                    return c;
            }

            return token.ToUpperInvariant() switch
            {
                "SPACE" => 0x20,
                "ENTER" or "RETURN" => 0x0D,
                "TAB" => 0x09,
                "ESC" or "ESCAPE" => 0x1B,
                "DELETE" or "DEL" => 0x2E,
                "INSERT" or "INS" => 0x2D,
                "HOME" => 0x24,
                "END" => 0x23,
                "PAGEUP" => 0x21,
                "PAGEDOWN" => 0x22,
                "UP" => 0x26,
                "DOWN" => 0x28,
                "LEFT" => 0x25,
                "RIGHT" => 0x27,
                "F1" => 0x70,
                "F2" => 0x71,
                "F3" => 0x72,
                "F4" => 0x73,
                "F5" => 0x74,
                "F6" => 0x75,
                "F7" => 0x76,
                "F8" => 0x77,
                "F9" => 0x78,
                "F10" => 0x79,
                "F11" => 0x7A,
                "F12" => 0x7B,
                _ => throw new FormatException($"Hotkey gesture '{gesture}' has an unrecognized key '{token}'."),
            };
        }

        public readonly bool Matches(int vkCode) =>
            vkCode == VkCode
            && IsDown(VK_CONTROL) == Ctrl
            && IsDown(VK_MENU) == Alt
            && IsDown(VK_SHIFT) == Shift
            && (IsDown(VK_LWIN) || IsDown(VK_RWIN)) == Win;

        private static bool IsDown(int vk) => (PInvoke.GetAsyncKeyState(vk) & 0x8000) != 0;
    }
}
