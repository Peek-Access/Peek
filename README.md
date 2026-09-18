<div align="center">
  <img
    align="center"
    src="logo.png"
    width="250"
  />
  <h1 align="center">Peek</h1>
  <p align="center">
  Another accessibility tool for Windows.
  </p>
</div>


## What it is

Peek is a Windows accessibility tool. It follows keyboard focus and mouse hover across any
application and announces what's there.


OCR is a fallback, not the primary mechanism. It only runs when UI Automation confirms an
element or an entire window exposes nothing usable - a real failure mode with some UI toolkits.
See [docs/OCR_STRATEGY.md](docs/OCR_STRATEGY.md) for when it runs and why.


## Accessibility

Peek's own interface targets EN 301 549 V3.2.1 Clause 11 ("Software"), which applies WCAG 2.1
Level AA to native software through the [WCAG2ICT](https://www.w3.org/WAI/standards-guidelines/wcag/non-web-ict/)
mapping, cross-checked against ISO 9241-171.



## Open-source dependencies


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
