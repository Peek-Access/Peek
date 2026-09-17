# Using Peek

Peek reads the screen out loud. This page covers day-to-day use.

## Focus tracking and hover tracking

Peek can follow your keyboard, your mouse, or both. Both are on by default, independently.

| | What it does | Who it's for |
| --- | --- | --- |
| **Focus tracking** | Announces whatever takes keyboard focus, in any application | Keyboard navigation, including without being able to see the screen |
| **Hover tracking** | Announces whatever is under the mouse pointer | Low-vision use, or sighted accessibility checks |

Both are in **Settings → Screen reading**. If you navigate by keyboard, turn hover tracking
off, or a stray mouse movement will interrupt what you're listening to.

## Keyboard shortcuts

Global, regardless of which window is active:

| Shortcut | What it does |
| --- | --- |
| `Ctrl+Alt+S` | Stop speaking |
| `Ctrl+Alt+T` | Toggle hover tracking |
| `Ctrl+Alt+D` | Describe the current element with AI (needs AI configured) |

Inside Peek's own window:

| Shortcut | Where | What it does |
| --- | --- | --- |
| `PageUp` / `PageDown` | Docked mode | Previous / next monitor page |
| `Space` | Element Inspector | Expand or collapse the selected node |
| `Enter` | App Monitor | Launch the selected app |
| `Delete` | Process Monitor | End the selected process |
| `Ctrl+E` | Process Monitor | Open the selected process's folder |
| `Escape` | Any dialog | Close it |

All editable in **Settings → Shell → Keyboard shortcuts**.

## The four pages

- **Screen Reader** - the main page: last announcement, running transcript, AI analysis panel.
- **Element Inspector** - the full accessibility tree of any window, not just what's under the
  cursor. Selecting a node speaks it, brings its window forward, and highlights it on screen.
- **App Monitor** - installed applications; `Enter` to launch.
- **Process Monitor** - running processes; `Delete` to end one.

Docked mode pins Peek to a screen edge; normal mode is an ordinary window. Switch in
**Settings → Shell** (takes effect after restart).

## Speech

**Voices.** Piper neural voices, running locally. The first time a voice is needed it
downloads; until then Peek speaks with the Windows built-in voices, so it works offline from
first launch.

**Languages.** UI language and speech language are set separately (**Settings → Language**,
**Settings → Speech**) - read the interface in one language, listen in another. Chinese text is
detected automatically and always spoken in Mandarin; everything else follows the speech
setting.

**Verbosity** (**Settings → Speech**) controls how much detail each announcement carries, from
just the name up to name, role, state, value, shortcut, and description.

## AI descriptions

When accessibility metadata isn't enough - an unlabeled icon, an image, a canvas-drawn app -
Peek can ask a language model to describe what's there. Off by default; turn it on in
**Settings → AI**.

Ollama runs locally; nothing leaves your machine. Any other provider (OpenAI, Anthropic,
Gemini, OpenRouter, a custom endpoint) sends a screenshot off-machine, which needs remote AI
turned on explicitly as a separate consent.

## Where Peek keeps things

| | Path |
| --- | --- |
| Settings | `%LOCALAPPDATA%\Peek\settings.json` |
| Logs | `%LOCALAPPDATA%\Peek\logs\` |
| Crash reports | `%LOCALAPPDATA%\Peek\crashes\` |
| Voice models | `%LOCALAPPDATA%\Peek\tts\models\` |
| Piper runtime | `%LOCALAPPDATA%\Peek\tts\piper\` |

Crash reports never leave your machine. Attaching the newest one helps if you file a bug.

## If something goes wrong

**Peek isn't speaking.** Check the connection dot on the Screen Reader page - if it's dark,
the worker process isn't running; restart Peek. If it's lit but nothing speaks, check the logs
folder: a first run with no network is the usual cause, and Peek should have fallen back to
Windows voices.

**Peek isn't announcing what I focus.** For other applications, check **Settings → Screen
reading → Announce whatever takes keyboard focus**. Peek's own interface has a separate switch,
**Settings → Accessibility → Announce Peek's own interface** (on by default - turn it off if
you're already running another always-on screen reader alongside Peek).

**Everything is announced twice.** You likely have both focus and hover tracking on, with the
mouse resting over what you're navigating. Turn off hover tracking.

**Windows warns about an unrecognized app when installing.** The installer isn't code-signed
yet. Choose "More info" then "Run anyway," or verify the download against the `SHA256SUMS.txt`
published with the release.
