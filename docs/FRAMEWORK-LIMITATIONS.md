# Accessibility gaps outside Peek's own code

Items here live in the Avalonia framework or the third-party `Pipboy.Avalonia` theme, not in
Peek's own XAML/C#. Tracked so they're not silently accepted, and so a dependency bump can be
checked against this list.

## Avalonia (currently pinned to 12.1.2)

1. **A bare `UserControl` only reports a generic "custom" automation role.**
   Fixed as of [PR #17480](https://github.com/AvaloniaUI/Avalonia/pull/17480) (Avalonia 11.3+,
   so Peek's 12.1.2 has it) - before that, a `UserControl` had no automation peer at all. Every
   `UserControl`-rooted view still needs an explicit `AutomationProperties.Name` (done - see
   ACCESSIBILITY.md), since "custom" alone tells a screen reader nothing. Nothing further to do
   unless Avalonia adds a way to override the reported control type for `UserControl` itself
   (per-instance override already exists via `AutomationProperties.ControlTypeOverride`, unused
   by Peek so far).

2. **Third-party custom controls get no automation peer unless they opt in.**
   `Control.OnCreateAutomationPeer()` defaults to `NoneAutomationPeer` - correct for decorative
   elements, but any genuinely interactive control must override it explicitly. Peek's own
   custom controls (`AnnouncementWave`, `LoadingIndicator`, `HighlightBorder`) are all
   non-interactive, so the default is fine there. Watch for this if a future control - Peek's or
   Pipboy's - is interactive and doesn't override it; it will be unreachable by keyboard/screen
   reader.

3. **`ControlAutomationPeer` subscribes to its owner without ever detaching.**
   [Issue #22232](https://github.com/AvaloniaUI/Avalonia/issues/22232), open upstream. A minor
   leak, not a compliance issue - noted so it isn't mistaken for a bug in `SelfFocusAnnouncer` if
   it shows up in a profiler.

4. **No cross-cutting focus-visual mechanism.**
   Unlike WPF's `FocusVisualStyle`, Avalonia needs a `:focus-visible` style per control type.
   Peek supplies its own (`Resources/AccessibilityFocus.axaml`) instead of relying on this
   existing for every control Pipboy themes - see the Pipboy section for why.

5. **`WindowDecorations.Full` + `ExtendClientAreaToDecorationsHint` breaks Tab navigation
   (WCAG 2.1.1).** With native min/max/close decorations kept and the client area extended into
   the title bar, Tab stopped moving focus at all in Peek's normal (non-docked) window. The raw
   `WM_KEYDOWN` isn't being swallowed at the Win32 message level (Avalonia's own
   `WindowImpl.AppWndProc` doesn't special-case decoration mode), so the break is somewhere
   higher in Avalonia's focus/Tab-navigation layer - not root-caused further than that. Closest
   upstream report: [issue #15593](https://github.com/AvaloniaUI/Avalonia/issues/15593) (a
   different symptom, a crash rather than inert Tab, from the same
   `ClearLogicalParent`/inherited-value-changed code path, labeled `by-design`). Workaround
   shipped: `MainWindow` now uses `WindowDecorations.BorderOnly` in both shell modes, with
   app-drawn minimize/maximize/restore/close buttons replacing the native ones
   (`MainWindow.axaml`/`.axaml.cs`).

## Pipboy.Avalonia (theme, currently 1.1.5-beta-preview.17) / Pipboy.Avalonia.ProDataGrid

Both are developed alongside Peek (source at `github.com/NeverMorewd/Pipboy.Avalonia` and its
`ProDataGrid` fork), so most items previously tracked here as upstream asks have since been
fixed directly - see git history for what changed.

1. **No confirmed accessible-contrast focus style for every control.**
   Peek doesn't rely on the theme's own focus treatment - `Resources/AccessibilityFocus.axaml`
   guarantees a 3:1+ outline regardless. The underlying `PipboyFocusBrush`/`PipboyBorderFocusBrush`
   color tokens do carry a WCAG-matching contrast floor now
   (`PipboyColorPalette.ApplyContrastFloor`) under the `AccessibleContrast`/`HighContrast`
   strategies - the one Peek selects at startup - but that's the color value, not proof every
   control's template renders a visible ring with it. If that's ever verified across every
   templated control, Peek's local override becomes redundant.

2. **Custom Pipboy controls beyond re-templated built-ins: confirmed, not assumed.**
   Both packages' full source was searched for `AutomationPeer`/`AutomationProperties`/
   `IsControlElement` - zero references. Pipboy re-templates Avalonia's built-in controls
   (Button, ToggleSwitch, ComboBox, ...) rather than introducing new control classes, so built-in
   automation peers survive the visual skin unchanged. If Pipboy or ProDataGrid ever ships a
   genuinely new interactive control class, it needs its own `AutomationPeer` override or it will
   be invisible to every screen reader, Peek's own self-reading included.

3. **ProDataGrid is currently pinned to a nightly build, not a stable release.**
   A recycled `DataGridRow` skipped re-applying its gridline brush after being detached and
   reattached (e.g. navigating away from a page and back), occasionally landing on a transient
   `null` from the same `ClearLogicalParent` sequence as issue #15593 above, with nothing
   correcting it afterward. Fixed upstream (`DataGrid.Template.cs`'s
   `InitializeElementsAfterReattach`, re-running `EnsureGridLines()`), published as
   `ProDataGrid 12.1.0.4-nightly.20260917.1`, which `Directory.Packages.props` currently pins.
   Move to the next stable release once it includes this commit.

## Using this list

When bumping either dependency, check its numbered items against the new version's changelog. An
item resolved upstream should be deleted here (and, for `AccessibilityFocus.axaml`, the local
workaround removed) rather than left to accumulate.
