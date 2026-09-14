# Peek

Peek is a local-first accessibility assistant: an Avalonia UI process and a C#
worker process (`Peek.Worker`) that provides UI Automation and text-to-speech
(PiperSharp) over a named-pipe JSON-RPC channel.

## Credits

See the root [README](../README.md#open-source-dependencies) for the full list with
licenses and upstream links. Exact pinned versions (see
`src/client/Directory.Packages.props` / `src/worker/Directory.Packages.props` for the
authoritative source):

### .NET client

| Dependency | Version |
| --- | --- |
| AsyncNavigation / AsyncNavigation.Avalonia | 2.0.2 |
| Avalonia / Avalonia.Desktop / Avalonia.Fonts.Inter / Avalonia.Themes.Simple | 12.1.2 |
| Irihi.Lingua | 1.0.0 |
| LibVLCSharp | 3.10.1 |
| Microsoft.Windows.CsWin32 | 0.3.333 |
| Pipboy.Avalonia / Pipboy.Avalonia.Fx | 1.1.2 |
| Pipboy.Avalonia.ProDataGrid | 1.0.0-beta |
| ProDataGrid | 12.1.0.4 |
| ReactiveUI | 24.2.0 |
| ReactiveUI.Avalonia | 12.1.2 |
| ReactiveUI.Primitives | 7.4.0 |
| ReactiveUI.SourceGenerators | 3.2.0 |
| SkiaSharp | 3.119.3-preview.1.1 |
| VideoLAN.LibVLC.Windows | 3.0.23.1 |

### .NET worker

| Dependency | Version |
| --- | --- |
| PiperSharp | 1.0.7 |
| Sdcb.SimdPaddleOCR / Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny | 1.2.0 / 1.0.0 |
| System.Drawing.Common | 10.0.12 |

