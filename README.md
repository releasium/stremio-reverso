# Stremio Reverso

Learn English while you watch: a small Windows tray app that sits over the **Stremio desktop app**. Hover a subtitle and the video pauses; hover a word to see its **Reverso Context** translation (English → Ukrainian), or select a phrase to translate it.

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)

## Features

- **Pause on hover.** Rest the cursor on a subtitle and Stremio pauses at once. Move it away and playback resumes.
- **Word lookup.** Hover any word to open its Reverso Context page, with translations and real example sentences.
- **Phrase and line translation.** Click or drag across words to translate a selection. Hover `⇄` at the end of a line to translate the whole line.
- **Your Reverso account.** Sign in once (Premium works) and every popup uses your account.
- **Stays out of the way.** It never steals focus from Stremio, and pauses or resumes only when it was the one that paused.

## How it works

Stremio draws subtitles inside its video player, so they aren't readable text. The app reads them off the screen instead:

1. While Stremio is in front, the app runs **Windows' built-in OCR** on the bottom of the Stremio window every 250 ms.
2. When the cursor touches a recognised subtitle line, it pauses Stremio by sending <kbd>Space</kbd>.
3. It then covers the frozen subtitle with invisible, hoverable word boxes and a `⇄` handle per line.
4. Translations open in a small popup with an embedded browser (WebView2) showing the real Reverso page.

While paused, an invisible shield stops Stremio from seeing mouse movement. That way its controls stay hidden and the subtitle doesn't jump.

## Requirements

- Windows 10 (19041+) or Windows 11
- [Stremio](https://www.stremio.com/downloads) desktop app (v5, `stremio-shell-ng`)
- English OCR language pack. It's usually preinstalled; if not, add it under **Settings → Time & language → Language & region**.
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/), which ships with Windows 11 and Stremio
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build

## Getting started

```bash
git clone git@github.com:releasium/stremio-reverso.git
cd stremio-reverso
dotnet run --project src/StremioReverso -c Release
```

A tray icon appears. On first run:

1. Right-click the tray icon and choose **Sign in to Reverso…**
2. Log in to your Reverso account, then close the window. The login is remembered.

To build a standalone executable:

```bash
dotnet publish src/StremioReverso -c Release -r win-x64 --self-contained false -o publish
```

Then run `publish\StremioReverso.exe`.

## Usage

| Action | Result |
| --- | --- |
| Hover a subtitle | Video pauses |
| Hover a word | Reverso Context page for that word |
| Click a word, or drag across words | Translate the selection (≤ 3 words → Reverso Context, longer → full translation) |
| Hover `⇄` after a line | Translate the whole line |
| 📌 in the popup | Keep the popup open |
| ↗ in the popup | Open the page in your browser |
| Move away from the subtitle and popup | Popup closes, video resumes |

Exit from the tray icon menu.

## Project structure

```
src/
  StremioReverso.Core/   pure logic, no Windows APIs: geometry, hit testing,
                         word normalisation, Reverso URLs, pause/resume state machine
  StremioReverso/        WPF tray app: Win32 interop, screen capture + OCR,
                         overlay, popup (WebView2), mouse shield
tests/
  StremioReverso.Tests/  xUnit tests for Core
docs/superpowers/        design spec and implementation plan
```

## Development

```bash
dotnet build
dotnet test
```

`HoverLogic` (in Core) makes every pause and resume decision from plain inputs, so behaviour changes can be covered by unit tests without Stremio running.

## Troubleshooting

- **Nothing pauses.** Make sure the Stremio window is in front, and that subtitles are large and clear enough to read. The tray tooltip says *waiting for Stremio* if the window isn't found.
- **Popup shows a "verify you are human" check.** Complete it in the popup once; Reverso remembers it.
- **Wrong word recognised.** OCR can misread stylised or very small subtitles. Bigger, plain subtitle styles in Stremio work best. You can also fix the text on the Reverso page.
- **Logs.** Errors are written to `%LOCALAPPDATA%\StremioReverso\log.txt`.

## Privacy

Screen capture and OCR run locally on your PC. Nothing is sent anywhere except the words you look up, which load from Reverso in the embedded browser. Your Reverso login is stored locally in `%LOCALAPPDATA%\StremioReverso\WebView2`.

## Limitations

- Only English → Ukrainian (set in `ReversoUrls`).
- If you have paused the video yourself, resting the cursor on a subtitle sends <kbd>Space</kbd> and resumes it.
- Not affiliated with Stremio or Reverso.
