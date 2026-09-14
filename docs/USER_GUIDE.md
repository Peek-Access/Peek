# Using Peek

Peek reads the screen out loud. This page covers how to drive it day to day.

## The two ways Peek follows you

Peek can follow your **keyboard**, your **mouse**, or both. They're independent, and both are
on by default.

| | What it does | Who it's for |
| --- | --- | --- |
| **Focus tracking** | Announces whatever takes keyboard focus — every control you Tab to, every menu item you arrow through, in any application | Anyone who navigates by keyboard, including without being able to see the screen |
| **Hover tracking** | Announces whatever is under the mouse pointer | Low-vision use, and sighted work like checking an app's accessibility |

Both are in **Settings → Screen reading**. If you navigate by keyboard, leave focus tracking
on and turn hover tracking off — otherwise moving the mouse will interrupt what you're
listening to.

## Keyboard shortcuts

These work **globally** — anywhere in Windows, whether or not Peek is the active window:

| Shortcut | What it does |
| --- | --- |
| `Ctrl+Alt+S` | **Stop speaking.** Cuts off the current announcement immediately |
| `Ctrl+Alt+T` | Toggle hover tracking on/off |
| `Ctrl+Alt+D` | Describe the current element using AI (needs AI configured) |

These work inside Peek's own window:

| Shortcut | Where | What it does |
| --- | --- | --- |
| `PageUp` / `PageDown` | Docked mode | Previous / next monitor page |
| `Space` | Element Inspector | Expand or collapse the selected node |
| `Enter` | App Monitor | Launch the selected app |
| `Delete` | Process Monitor | End the selected process |
| `Ctrl+E` | Process Monitor | Open the selected process's folder |
| `Escape` | Any dialog | Close it |

All of these are editable in **Settings → Shell → Keyboard shortcuts**.

## The four pages

- **Screen Reader** — the main page. Shows what was last announced, with a running transcript
  of everything Peek has said, and the AI analysis panel.
- **Element Inspector** — browses the full accessibility tree of any window, not just what's
  under the cursor. Selecting a node speaks it, brings its window forward, and highlights it
  on screen.
- **App Monitor** — installed applications; select to hear details, `Enter` to launch.
- **Process Monitor** — running processes; select to hear details, `Delete` to end one.

In docked mode Peek pins itself to a screen edge and other windows resize around it. In
normal mode it's an ordinary window. Switch in **Settings → Shell** (takes effect after a
restart).

## Speech

**Voices.** Peek uses Piper neural voices, running locally. The first time it needs a voice
it downloads it — until that finishes, Peek speaks with the Windows built-in voices instead,
so it works from the first launch even offline. Nothing is lost if you're offline; you just
get the Windows voices until the better ones arrive.

**Languages.** The UI language and the speech language are set separately
(**Settings → Language** and **Settings → Speech**). You can read the interface in one
language and listen in another. Chinese text is detected automatically and spoken in Mandarin
regardless of the speech-language setting; everything else follows that setting.

**Verbosity** (**Settings → Speech**) controls how much detail each announcement carries —
from just the name, up to name, role, state, value, shortcut, and description.

## AI descriptions

When accessibility metadata isn't enough — an unlabeled icon, an image, a canvas-drawn app —
Peek can ask a language model to describe what's there. This is off unless you configure it
in **Settings → AI**.

Ollama runs locally and nothing leaves your machine. Any other provider (OpenAI, Anthropic,
Gemini, OpenRouter, or your own endpoint) sends a screenshot off your machine, so it requires
turning on remote AI explicitly as a second, separate consent.

## Where Peek keeps things

| | Path |
| --- | --- |
| Settings | `%LOCALAPPDATA%\Peek\settings.json` |
| Logs | `%LOCALAPPDATA%\Peek\logs\` |
| Crash reports | `%LOCALAPPDATA%\Peek\crashes\` |
| Voice models | `%LOCALAPPDATA%\Peek\tts\models\` |
| Piper runtime | `%LOCALAPPDATA%\Peek\tts\piper\` |

Crash reports stay on your machine — nothing is transmitted. If you file a bug, attaching the
newest one from that folder helps.

## If something goes wrong

**Peek isn't speaking.** Check the connection dot on the Screen Reader page — if it's not
lit, the worker process isn't running. Restart Peek. If it still doesn't speak, look in the
logs folder above: a first run with no network is the most common cause, and Peek should have
fallen back to Windows voices.

**Peek isn't announcing what I focus.** Check **Settings → Screen reading → Announce whatever
takes keyboard focus**. Note that Peek deliberately never announces its own window.

**Everything is announced twice.** You likely have both focus and hover tracking on and the
mouse is resting over what you're navigating. Turn off hover tracking.

**Windows warns about an unrecognized app when installing.** The installer isn't code-signed
yet. Choose "More info" then "Run anyway", or verify the download against the `SHA256SUMS.txt`
published with the release.
