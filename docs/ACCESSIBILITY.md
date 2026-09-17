# Accessibility conformance

Peek is a screen reader. If its own interface isn't usable by the audience it exists for,
nothing else about it matters - so this document treats Peek's own UI as a compliance target,
not just the thing that reads other apps' UIs. It covers which standards Peek is built against,
what that means concretely, and what's out of Peek's hands.

## Standards this targets

Peek is not a web app, so WCAG on its own doesn't directly apply - a desktop application is
covered through the standards that translate WCAG's success criteria into non-web terms.

| Standard | Scope | How Peek uses it |
| --- | --- | --- |
| **EN 301 549 V3.2.1 (2021-03), Clause 11 ("Software")** | The EU-harmonized standard for ICT accessibility; Clause 11 covers native/non-web software by applying WCAG 2.1's success criteria through the [WCAG2ICT](https://www.w3.org/WAI/standards-guidelines/wcag/non-web-ict/) mapping (e.g. "page" -> "screen/view", "web page" -> "software"). | Primary conformance target for every view in Peek.UI. |
| **WCAG 2.1, Level AA** | The underlying success criteria EN 301 549 Clause 11 re-applies to software. | Read as: 1.1.1, 1.3.1, 1.4.3, 1.4.11, 2.1.1, 2.1.2, 2.4.3, 2.4.7, 3.2.1, 3.3.2, 4.1.2, 4.1.3, applied per-control instead of per-page. |
| **ISO 9241-171:2008** | "Ergonomics of human-system interaction - Guidance on software accessibility." International, technology-neutral software accessibility guidance, commonly cited alongside EN 301 549 for desktop software specifically. | Cross-checked for the keyboard-operability and consistency guidance that EN 301 549 states more tersely. |
| **Germany: BFSG** (*Barrierefreiheitsstärkungsgesetz*), in force since 2025-06-28 | Germany's transposition of the EU's Accessibility Act (Directive (EU) 2019/882). Points to WCAG 2.2 AA / EN 301 549. | Peek targets this voluntarily; see the scope note below. |
| **Germany: BITV 2.0** (*Barrierefreie-Informationstechnik-Verordnung*) | Germany's public-sector digital accessibility ordinance; references EN 301 549 V3.2.1 directly. | Same target as EN 301 549 Clause 11 above - Peek conforms to the same underlying clause BITV 2.0 points to. |

**Scope note:** the BFSG's mandatory categories (EAA Annex I - consumer hardware/OS bundles,
self-service terminals, e-commerce, banking, e-books, ...) don't cleanly include a standalone,
free accessibility utility like Peek. Nothing here should be read as a legal conformance claim -
it's a voluntary commitment to the same clause (EN 301 549 §11 / WCAG2ICT) those laws point to,
because it is the right bar regardless of whether it's legally required for this kind of app.

EN 301 549 V4.1.1 (ETSI, published 2026-09-02) moves the same clauses to WCAG 2.2 AA. Peek isn't
re-targeted to it yet; tracked as follow-up work once the WCAG2ICT mapping for 2.2 is final.

## What this looks like in the codebase

- **Every focusable control has a name, role, and state a screen reader can read.** Enforced via
  `AutomationProperties.Name`/`LabeledBy` in XAML (see `SettingsView.axaml` for the label-pairing
  pattern) - this is WCAG 4.1.2 (Name, Role, Value) and 1.3.1 (Info and Relationships) applied to
  software controls instead of HTML.
- **Every page identifies itself.** Each top-level view (`ScreenReaderView`, `SettingsView`, ...)
  sets `AutomationProperties.Name` and a `LandmarkType` (`Main`, `Form`, `Navigation`,
  `ContentInfo`) on its root, and `MainView`'s nav sidebar is a `Navigation` landmark - the
  non-web equivalent of WCAG 2.4.1 (Bypass Blocks) and 2.4.6 (Headings and Labels): a screen
  reader user can identify where they are without reading the whole screen.
- **Nothing is keyboard-inaccessible.** Every mouse-only affordance (double-click to launch/kill
  a process, drag-to-dock) has a keyboard equivalent, configurable in Settings > Keyboard
  Shortcuts (WCAG 2.1.1, Keyboard). Modal windows close on Escape (WCAG 2.1.2, No Keyboard Trap).
- **Focus is always visible**, independent of the active theme color -
  `Resources/AccessibilityFocus.axaml` guarantees a high-contrast outline (WCAG 2.4.7, Focus
  Visible; 1.4.11, Non-text Contrast) regardless of which primary color the user has picked in
  Settings > Appearance.
- **Status messages don't require focus to be heard.** Announcements, errors, and AI narration go
  through `IAccessibilitySpeechService` rather than only updating on-screen text, satisfying
  WCAG 4.1.3 (Status Messages) - a spoken announcement is Peek's own equivalent of an ARIA live
  region.
- **Text contrast exceeds AA in every requested Pipboy palette color** (see
  `PIPBOY_THEME_ADJUSTMENTS.md`) - main text and dim text both clear 7:1 against every
  background/surface combination, well past the 4.5:1 WCAG 1.4.3 minimum for normal text.
- **Peek reads itself.** See "Self-reading" below - this is the part that doesn't map onto an
  existing WCAG criterion, because WCAG assumes an external assistive technology exists at all.
- **Multi-language is not a skin.** English, German, and Chinese are first-class throughout,
  including in what gets spoken (`SpeechStrings`, resolved from the TTS-language setting rather
  than the UI language) - hardcoded strings that bypassed this were bugs, not exceptions (see
  git history on this branch for several fixed).

## Self-reading

A screen reader that can't read its own Settings screen isn't usable by someone who can't see.
Peek's main pipeline (`FocusTracker` -> `FocusAnnouncer`) deliberately excludes Peek's own
process, because it resolves focus through a system-wide UI Automation query that could pick up
incidental focus churn Peek's own announcement UI causes. That exclusion is correct for that
pipeline, but it left Peek's own UI silent.

`Peek.UI.Services.SelfFocusAnnouncer` covers that gap directly: it listens to Avalonia's own
managed `GotFocus` event (not the raw OS event stream), reads whatever automation peer the
focused control already exposes - the exact same accessible name/role/state a real screen reader
would see - and speaks it through the identical formatting pipeline (`ISpeechPolicy`) external
apps get ("Login, button"). Because it only fires for genuine Tab-stop/click targets and nothing
in Peek's own announcement views ever calls `Focus()` on itself, there's no path back into a
feedback loop.

Two navigation paths move to a new page without moving keyboard focus at all - the docked shell's
PageUp/PageDown monitor cycling, and opening Settings from the tray icon - so those are announced
explicitly (`DockShellViewModel.AnnounceCurrentPage`, `App.axaml.cs`'s tray Settings handler)
rather than relying on a focus event that never fires.

Self-reading is on by default (Settings > Accessibility > "Announce Peek's own interface") so
Peek is usable standalone. A user who pairs Peek with another always-on screen reader can turn it
off to avoid hearing every control named twice.

## What's outside Peek's control

Some gaps live in the Avalonia framework or the third-party Pipboy.Avalonia theme, not in Peek's
own code - see [FRAMEWORK-LIMITATIONS.md](FRAMEWORK-LIMITATIONS.md) for the itemized list and what
to watch for as those dependencies update.

## Verifying this

- `dotnet test test/Peek.Core.Tests` covers the speech-formatting and settings logic this
  document describes, but not the actual on-screen rendering.
- Structural/contrast claims involving Pipboy's requested color tokens were checked against the
  WCAG relative-luminance formula (see `docs/PIPBOY_THEME_ADJUSTMENTS.md` for the source values);
  they were not re-measured against Pipboy.Avalonia's actual shipped colors, which weren't
  independently inspectable from source. Re-verify with a contrast analyzer (e.g. the Windows
  Accessibility Insights color contrast tool) once the theme's exact runtime palette is
  confirmed.
- Screen-reader testing (NVDA/Narrator reading Peek's own window) has not been performed as part
  of this change; `SelfFocusAnnouncer` was verified by unit-testable logic and a full build, not
  by a live NVDA session. Treat as the next verification step before calling this feature-complete.
