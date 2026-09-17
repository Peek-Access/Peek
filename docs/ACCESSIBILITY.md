# Accessibility conformance

Peek is a screen reader, so its own interface is a compliance target, not just the thing that
reads other apps' UIs. This covers which standards Peek is built against, what that means in
practice, and what's outside Peek's control.

## Standards

Peek is a desktop app, not a web app, so WCAG applies through the standards that translate its
success criteria into non-web terms.

| Standard | Scope | How Peek uses it |
| --- | --- | --- |
| **EN 301 549 V3.2.1 (2021-03), Clause 11 ("Software")** | EU-harmonized ICT accessibility standard; Clause 11 applies WCAG 2.1 to native software via the [WCAG2ICT](https://www.w3.org/WAI/standards-guidelines/wcag/non-web-ict/) mapping ("page" → "screen/view", etc.). | Primary conformance target for every view in Peek.UI. |
| **WCAG 2.1, Level AA** | The success criteria EN 301 549 Clause 11 re-applies to software. | 1.1.1, 1.3.1, 1.4.3, 1.4.11, 2.1.1, 2.1.2, 2.4.3, 2.4.7, 3.2.1, 3.3.2, 4.1.2, 4.1.3, applied per-control. |
| **ISO 9241-171:2008** | Technology-neutral software accessibility guidance. | Cross-checked for keyboard-operability and consistency detail EN 301 549 states more tersely. |
| **Germany: BFSG**, in force since 2025-06-28 | Germany's transposition of the EU Accessibility Act. Points to WCAG 2.2 AA / EN 301 549. | Voluntary target; see scope note. |
| **Germany: BITV 2.0** | Germany's public-sector digital accessibility ordinance; references EN 301 549 V3.2.1 directly. | Same target as the EN 301 549 row above. |

**Scope note:** the BFSG's mandatory categories (EAA Annex I - consumer hardware/OS bundles,
self-service terminals, e-commerce, banking, e-books) don't cleanly cover a standalone free
utility like Peek. This isn't a legal conformance claim - it's a voluntary commitment to the
same clause those laws point to.

EN 301 549 V4.1.1 (ETSI, 2026-09-02) moves the same clauses to WCAG 2.2 AA. Peek isn't
re-targeted yet; follow-up once the 2.2 WCAG2ICT mapping is final.

## In the codebase

- **Every focusable control has a name, role, and state.** `AutomationProperties.Name`/
  `LabeledBy` in XAML (see `SettingsView.axaml` for the label-pairing pattern) - WCAG 4.1.2 and
  1.3.1 applied to software controls.
- **Every page identifies itself.** Each top-level view sets `AutomationProperties.Name` and a
  `LandmarkType` (`Main`, `Form`, `Navigation`, `ContentInfo`); `MainView`'s nav sidebar is a
  `Navigation` landmark. WCAG 2.4.1 and 2.4.6 for software.
- **Nothing is keyboard-inaccessible.** Every mouse-only affordance (double-click to launch/kill
  a process, drag-to-dock) has a keyboard equivalent, editable in Settings → Keyboard Shortcuts
  (WCAG 2.1.1). Modal windows close on Escape (WCAG 2.1.2). `MainWindow` uses
  `WindowDecorations.BorderOnly` in both shell modes - `WindowDecorations.Full` (native title
  bar) silently broke Tab navigation in normal window mode; see FRAMEWORK-LIMITATIONS.md.
- **Focus is always visible**, independent of theme color - `Resources/AccessibilityFocus.axaml`
  guarantees a high-contrast outline (WCAG 2.4.7, 1.4.11) on top of whatever the theme does.
- **Status messages don't require focus to be heard.** Announcements, errors, and AI narration
  go through `IAccessibilitySpeechService` (WCAG 4.1.3) - Peek's equivalent of an ARIA live
  region.
- **Contrast is enforced at the palette level.** Peek selects
  `PipboyPaletteStrategy.AccessibleContrast` at startup (`App.axaml.cs`); `PipboyColorPalette`
  floors text/status colors to 4.5:1 against the surface (WCAG 1.4.3) and borders/focus
  indicators to 3:1 (WCAG 1.4.11), regardless of the primary color picked in Settings →
  Appearance.
- **Peek reads itself.** See Self-reading below - the one item here with no direct WCAG
  equivalent, since WCAG assumes an external assistive technology exists.
- **Multi-language, not a skin.** English, German, and Chinese throughout, including speech
  (`SpeechStrings`, resolved from the TTS-language setting, not the UI language).

## Self-reading

Peek's main pipeline (`FocusTracker` → `FocusAnnouncer`) excludes Peek's own process on
purpose - it resolves focus through a system-wide UI Automation query, which could pick up
focus churn Peek's own announcement UI causes. That left Peek's own UI silent.

`Peek.UI.Services.SelfFocusAnnouncer` covers that gap separately: it listens to Avalonia's own
managed `GotFocus` event, reads the automation peer the focused control already exposes, and
speaks it through the same formatting pipeline (`ISpeechPolicy`) external apps get
("Login, button"). It only fires for a genuine Tab-stop/click target, and nothing in Peek's own
announcement views calls `Focus()` on itself, so there's no feedback loop.

Two navigation paths change page without moving keyboard focus - docked mode's PageUp/PageDown
cycling, and opening Settings from the tray icon - so those are announced explicitly
(`DockShellViewModel.AnnounceCurrentPage`, the tray Settings handler in `App.axaml.cs`) instead
of relying on a focus event that never fires.

On by default (Settings → Accessibility → "Announce Peek's own interface"), so Peek is usable
standalone. Turn it off if pairing Peek with another always-on screen reader, to avoid hearing
every control named twice.

## Outside Peek's control

See [FRAMEWORK-LIMITATIONS.md](FRAMEWORK-LIMITATIONS.md) for gaps in Avalonia or the
`Pipboy.Avalonia` theme, tracked so a dependency bump can be checked against them.

## Verifying this

- `dotnet test test/Peek.Core.Tests` covers speech-formatting and settings logic, not on-screen
  rendering.
- Contrast claims are backed by `Pipboy.Avalonia`'s `WcagContrast`/`PipboyColorPalette` source
  (real relative-luminance math), checked directly against that package's source. Still worth a
  spot-check with a contrast analyzer (e.g. Accessibility Insights) against the rendered UI,
  since correct source doesn't guarantee every control's template applies it.
- Screen-reader testing (NVDA/Narrator against Peek's own window) hasn't been done -
  `SelfFocusAnnouncer` has no automated test coverage either. Next verification step before
  calling this feature complete.
