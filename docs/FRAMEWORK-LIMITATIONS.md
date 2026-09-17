# Accessibility gaps outside Peek's own code

Items here can't be fixed by editing Peek's XAML/C# - they're either in the Avalonia framework
itself or in the third-party `Pipboy.Avalonia` theme package Peek depends on for its whole visual
language (see the "Open-source dependencies" table in the root README). Listed so they're tracked
rather than silently accepted, and so a future dependency bump can be checked against this list.

## Avalonia (currently pinned to 12.1.2)

1. **A bare `UserControl` only reports a generic "custom" automation role.**
   Fixed as of [PR #17480](https://github.com/AvaloniaUI/Avalonia/pull/17480) (Avalonia 11.3+,
   so Peek's 12.1.2 has it) - before that fix, a `UserControl` had *no* automation peer at all
   and was completely invisible to a screen reader. Peek is past the invisible case, but every
   `UserControl`-rooted view still needs an explicit `AutomationProperties.Name` (done - see
   `ACCESSIBILITY.md`) because "custom" alone tells a screen-reader user nothing. Nothing further
   to do here unless Avalonia adds a way to override the reported control type itself for
   `UserControl` (it can be done per-instance via `AutomationProperties.ControlTypeOverride`,
   which Peek doesn't currently need but is available if a future view needs a more specific
   role than a landmark name provides).

2. **Third-party custom controls get no automation peer unless they opt in.**
   `Control.OnCreateAutomationPeer()` defaults to `NoneAutomationPeer` - correct and desirable
   for purely decorative elements (a lot of Peek's own Borders/Ellipses/Panels benefit from this:
   they're invisible to assistive tech by default, which is exactly what a screen reader should
   see for non-interactive decoration), but it means any control that *is* meant to be
   interactive must explicitly override it. Peek's own custom controls
   (`AnnouncementWave`, `LoadingIndicator`, `HighlightBorder`) are all non-interactive
   visualizations, so the default is correct for them and no override was added. Watch for this
   if either Peek or a future Pipboy update introduces a genuinely interactive custom control -
   it will silently be unreachable by keyboard/screen reader unless someone remembers to give it
   a peer.

3. **`ControlAutomationPeer` subscribes to its owner without ever detaching.**
   [Issue #22232](https://github.com/AvaloniaUI/Avalonia/issues/22232), open upstream. A minor
   memory-leak-shaped bug, not a compliance blocker - noted here only so it isn't mistaken for
   something Peek's own `SelfFocusAnnouncer` is doing wrong if it ever shows up in a profiler.

4. **No cross-cutting "focus visual" mechanism.**
   Unlike WPF's `FocusVisualStyle` (which auto-applies an adorner to any control), Avalonia
   requires a `:focus-visible` style per control type. Peek supplies its own
   (`Resources/AccessibilityFocus.axaml`) rather than depending on this existing for every
   control Pipboy themes - see the Pipboy section below for why that file exists at all instead
   of just fixing the theme's own focus style.

## Pipboy.Avalonia (theme, currently 1.1.4-beta) / Pipboy.Avalonia.ProDataGrid

Per this repo's existing convention (see `PIPBOY_THEME_ADJUSTMENTS.md`), visual/theme changes
belong upstream in the theme package, not as Peek-local overrides, so every consumer of the theme
benefits equally. The items below are additions to that request list, specifically for
accessibility:

1. **The theme's border token fails WCAG 1.4.11 (Non-text Contrast).**
   `PIPBOY_THEME_ADJUSTMENTS.md` requests `Border: #254634`. Checked against the WCAG
   relative-luminance formula, `#254634` against the requested background (`#071810`) or surface
   (`#0B2017`) computes to roughly **1.6-1.75:1** - well under the 3:1 minimum required for a UI
   component boundary that's necessary to identify the component (a required input field's edge,
   a focus indicator). It's fine for a purely decorative divider, which is most of its current
   use in Peek, but it is not safe to reuse for anything that needs to actually be seen.
   **Ask:** either raise this token's contrast, or keep it decorative-only and give focus/required
   states their own token that's verified against 3:1 (the theme does define separate
   `PipboyFocusBrush`/`PipboyBorderFocusBrush` resources for this - their actual color values
   weren't inspectable from the compiled package and should be checked the same way).

2. **No confirmed accessible-contrast focus style ships with the theme for every control.**
   Peek doesn't depend on the theme's own focus treatment being sufficient - see item 4 above and
   `Resources/AccessibilityFocus.axaml`, which guarantees a 3:1+ outline independent of whatever
   the theme does. **Ask:** if/when the theme's own focus contrast is verified and fixed, Peek's
   local override becomes redundant and can be deleted - track that as a cleanup opportunity
   rather than carrying both indefinitely.

3. **Custom Pipboy controls beyond re-templated built-ins weren't independently auditable.**
   The theme's compiled DLL was checked for `AutomationPeer`/`AutomationProperties`/
   `IsControlElement` references and found none - meaning Pipboy purely re-templates Avalonia's
   *built-in* controls (Button, ToggleSwitch, ComboBox, ...) rather than introducing new control
   classes, which is good: built-in automation peers survive a template swap regardless of the
   visual skin. This could not be fully confirmed against the theme's actual source (only
   available as a compiled NuGet package here), so this is a documented assumption, not a
   verified fact. **Ask:** if Pipboy or ProDataGrid ever ships a genuinely new interactive
   control class, it needs its own `AutomationPeer` override (see Avalonia item 2 above) or it
   will be invisible to every screen reader, Peek's own self-reading included.

4. **Tray/context menu styling gap already tracked.**
   Not accessibility-specific, but relevant to the same "ask upstream, don't patch locally"
   principle - see `PIPBOY_THEME_ADJUSTMENTS.md`'s "Tray menu" section for the existing
   `Separator`/`MenuItem` styling request.

## How to use this list

When bumping either dependency, check this file's numbered items against the new version's
changelog/release notes. An item resolved upstream should be deleted here (and, for the
`AccessibilityFocus.axaml` case, the local workaround removed) rather than left to accumulate.
