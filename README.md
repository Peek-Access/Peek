<div align="center">
  <img
    align="center"
    src="logo.png"
    width="250"
  />
  <h1 align="center">Peek</h1>
  <p align="center">
  Not just a screen reader — a way to see your screen, however you need to.
  </p>
</div>


## Features

- **Follows your keyboard** — every control you Tab to, in any application, announced as you reach it; you don't have to be able to see where to point a mouse
- **Follows your mouse** — hover any on-screen element to hear what it is, instantly, via native UI Automation
- **Considered announcements, not a read-out** — role and state are only spoken when they add information ("Login, button" — not a recitation of every property), hover and focus are throttled and de-duplicated so fast mouse movement doesn't turn into noise, and a user-requested announcement always takes priority over ambient chatter — nothing queues up to talk over you after you've already moved on.
- **Element Inspector** — browse the full UI tree of any window, not just what's under the cursor
- **AI screen & window analysis** — ask an LLM (OpenAI, Anthropic, Gemini, OpenRouter, Ollama or your own endpoint) to describe what's on screen when accessibility metadata alone isn't enough
- **OCR fallback** — reads text out of images and non-accessible UI when there's nothing else to go on
- **Local, offline text-to-speech** — neural voices via Piper, no cloud dependency, no per-character billing
- **Multi-language out of the box** — English, German, and Chinese, with automatic language detection per utterance
- **App & Process monitors** — launch, inspect, and manage running software without leaving the keyboard
- **Dockable shell** — pin Peek to a screen edge as your home base on screen: whenever things get disorienting elsewhere, it's the one region that never moves and is always there to come back to.

## Accessibility & compliance

Peek is a screen reader, so its own interface is held to the same bar it holds every other
application to - including reading itself, not just what's underneath it. Peek targets:

- **[EN 301 549](https://www.etsi.org/deliver/etsi_en/301500_301599/301549/03.02.01_60/en_301549v030201p.pdf) V3.2.1, Clause 11** ("Software") - the EU-harmonized ICT accessibility standard, applying **WCAG 2.1 Level AA** to native software via the [WCAG2ICT](https://www.w3.org/WAI/standards-guidelines/wcag/non-web-ict/) mapping (WCAG itself is written for the web; EN 301 549 Clause 11 is the non-web equivalent this project actually conforms to)
- **ISO 9241-171** - international software-accessibility guidance, cross-checked alongside EN 301 549
- **Germany's BFSG** (*Barrierefreiheitsstärkungsgesetz*, in force since 2025-06-28) and **BITV 2.0** (*Barrierefreie-Informationstechnik-Verordnung*), both of which point back to the same EN 301 549 clause above

Every focusable control has a name, role, and state; every page identifies itself to assistive
technology; focus is always visible at guaranteed contrast regardless of theme color; nothing
requires a mouse; and Peek announces its *own* interface as you navigate it - the same
follow-the-keyboard model it gives every other application - so it's usable standalone by someone
who can't see it either.

See [docs/ACCESSIBILITY.md](docs/ACCESSIBILITY.md) for the full conformance statement and
[docs/FRAMEWORK-LIMITATIONS.md](docs/FRAMEWORK-LIMITATIONS.md) for the handful of gaps that live
in the Avalonia framework or the third-party Pipboy theme rather than in Peek's own code.

## Open-source dependencies

Peek is built on the following open-source projects:

| Project | License |
| --- | --- |
| [Avalonia](https://avaloniaui.net/) | MIT |
| [Pipboy.Avalonia](https://github.com/NeverMorewd/Pipboy.Avalonia) (+ .Fx, .ProDataGrid) | MIT |
| [ProDataGrid](https://github.com/wieslawsoltes/ProDataGrid) | MIT |
| [AsyncNavigation](https://github.com/NeverMorewd/AsyncNavigation) | MIT |
| [ReactiveUI](https://reactiveui.net/) (+ .Avalonia, .SourceGenerators) / [ReactiveUI.Primitives](https://github.com/reactiveui/Primitives) | MIT |
| [Microsoft.Extensions.*](https://github.com/dotnet/runtime) | MIT |
| [Microsoft.Windows.CsWin32](https://github.com/microsoft/CsWin32) | MIT |
| [Irihi.Lingua](https://github.com/irihitech/Irihi.Lingua) | MIT |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | MIT |
| [System.Drawing.Common](https://github.com/dotnet/winforms) | MIT |
| [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp) / [VideoLAN.LibVLC.Windows](https://code.videolan.org/videolan/libvlc-nuget) | LGPL-2.1-or-later |
| [PiperSharp](https://github.com/Lyx52/PiperSharp) / [Piper](https://github.com/rhasspy/piper) | MIT |
| [Sdcb.SimdPaddleOCR](https://github.com/sdcb/SimdPaddleOCR) / [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) | Apache-2.0 |

## License

GPL-3.0 — see [LICENSE](LICENSE). Peek is free for everyone: individuals,
companies, government agencies, and non-profits alike. Government and
non-profit organizations can also get free deployment/usage guidance, and
companies can get paid custom development, integration, and consulting — see
[Licensing & Support](LICENSING_AND_SUPPORT.md) for details.
