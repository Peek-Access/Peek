# Pipboy theme adjustments for Peek

Peek's views now use the existing Pipboy brushes and typography resources. The client does not add
global theme overrides. The following changes belong in `Pipboy.Avalonia` so every consumer receives
the same visual language.

## Requested tokens

| Area | Current direction | Suggested value |
| --- | --- | --- |
| App background | Deep green-black | `#071810` |
| Surface | Slightly lifted green | `#0B2017` |
| High surface | Toolbar / focused surface | `#102A1C` |
| Primary | Bright accessible green | `#27FF72` |
| Main text | Soft light green | `#E2F1E7` |
| Dim text | Muted green-gray | `#90AD9A` |
| Border | Low-contrast green | `#254634` |

## Typography

Use a humanist sans-serif such as Segoe UI for titles, descriptions, controls, and table content.
Keep the existing monospace family for compact metadata, counters, and diagnostic values where
column alignment matters.

## Shape and spacing

- Panel radius: about 16 px.
- Control radius: about 8 px.
- Pill radius: fully rounded.
- Keep panel borders one pixel and visibly quieter than the primary text.

## Acceptance checks

- Existing resource names remain stable (`Pipboy*Brush`, `PipboyFontFamily`, and corner-radius
  resources), so Peek's views need no per-control overrides.
- Text and border contrast remains readable in both normal and docked window modes.
- Focus, hover, and disabled states use the same tokens instead of introducing client-only colors.

## Tray menu

Peek creates the menu with Avalonia `TrayIcon` / `NativeMenu` in `App.axaml.cs`. On Windows,
Avalonia presents a managed popup for the tray menu, so the normal `MenuFlyoutPresenter`, `MenuItem`,
and `Separator` styles apply. Sidebar Diagnostics has these styles; Peek currently relies on the
default separator, which is why it can appear black against the Pipboy surface.

Add the following to the shared Pipboy menu theme rather than adding a Peek-only override:

- `MenuFlyoutPresenter`: Pipboy surface background, border brush, one-pixel border, and panel radius.
- `MenuItem`: Pipboy text brush, surface/control background, and hover brush.
- `Separator`: one-pixel height, small vertical margins, and `PipboyBorderBrush` (or a subdued
  primary brush) as its background.

On platforms where Avalonia uses a truly native menu, these visual styles may not be available;
the Windows tray popup is the path relevant to this issue.

## Accessibility: border token contrast

The requested `Border: #254634` token computes to roughly 1.6-1.75:1 against the requested
background/surface colors - well under the 3:1 WCAG 1.4.11 minimum for a UI component boundary
that needs to be seen (a focus indicator, a required field's edge). Fine for a purely decorative
divider; not safe to reuse for anything else. See `ACCESSIBILITY.md` and
`FRAMEWORK-LIMITATIONS.md` for the full writeup and the client-side workaround
(`Resources/AccessibilityFocus.axaml`) Peek carries until this is verified/fixed upstream.
