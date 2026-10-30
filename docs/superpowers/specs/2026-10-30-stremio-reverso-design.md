# Stremio Reverso — design

A small Windows tray app that sits over the **Stremio desktop app**. When you hover the subtitles, it pauses the video, reads the subtitle text off the screen with Windows' built-in OCR, and shows a Reverso translation (English → Ukrainian): of the hovered **word**, or of the **whole line**.

## Goals
- The cursor resting on subtitle text in Stremio pauses playback. Moving away resumes it, but only if the app paused it.
- Hover a word → popup with Reverso Context translations and 2–3 example sentence pairs.
- Hover the `⇄` handle at the end of a line → popup with the translation of the whole line.
- Works windowed and fullscreen, with whatever subtitles Stremio shows (built-in, addon, styled).
- Never blocks clicks on Stremio while the video is playing.

## Non-goals
- Other players, other language pairs (the pair is one constant: `english-ukrainian`), a settings UI, or a saved vocabulary.
- Installer or auto-update. It runs from the build output, with an optional "start with Windows" shortcut added later.

## Tech
- .NET 8 WPF (C#), target `net8.0-windows10.0.19041.0` for WinRT access.
- OCR: `Windows.Media.Ocr` with English (en-US), already installed on this PC.
- Win32 interop: `GetCursorPos`, `GetForegroundWindow`, `GetWindowRect`, `SendInput`, plus `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` overlay windows.
- **Prerequisite:** the .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`). It isn't installed yet.

## Architecture
```
src/StremioReverso/
  App.xaml(.cs)            tray icon (Exit), starts the HoverWatcher
  Win32/Native.cs          P/Invoke declarations
  Stremio/StremioWindow.cs finds the stremio-shell-ng window, its rect, whether it's foreground
  Stremio/Playback.cs      Pause()/Resume() by sending Space via SendInput; tracks "we paused"
  Capture/ScreenGrab.cs    copies a screen rect to a SoftwareBitmap (2x upscale + grayscale)
  Ocr/SubtitleOcr.cs       OCR → List<OcrLineBox{Text, Rect, Words[{Text, Rect}]}> in screen coords
  Core/HitTest.cs          pure: given lines + cursor point → hovered word / line / none
  Core/WordNormalizer.cs   pure: strips punctuation, keeps contractions/hyphens, lowercases
  Core/HoverWatcher.cs     state machine (below), 30 Hz cursor poll on a DispatcherTimer
  Reverso/ReversoClient.cs HttpClient calls + LRU cache
  Reverso/ReversoParser.cs pure: JSON → WordResult{Translations, Examples} / LineResult{Text}
  UI/WordOverlay.xaml      transparent, no-activate window over the frozen subtitle: word highlight rects + ⇄ handles
  UI/TranslationPopup.xaml no-activate popup: translations, examples, "Open in Reverso", pin
tests/StremioReverso.Tests/  xUnit: HitTest, WordNormalizer, ReversoParser (saved JSON fixtures)
```

## Behaviour (HoverWatcher state machine)
1. **Idle.** Stremio isn't foreground, or the cursor is outside the *subtitle zone* (the bottom 35% of Stremio's window). Nothing happens.
2. **Dwelling.** The cursor has been still in the zone for 250 ms → grab the zone and OCR it (~100–200 ms).
   - If the cursor isn't within 12 px of any OCR'd line → back to Idle (with a 1 s cooldown before the next OCR).
3. **Paused.** The cursor is on a line → `Playback.Pause()` (Space). Re-OCR the now-frozen frame for a clean result, then show **WordOverlay**: a highlight box per word and a `⇄` handle after each line. The overlay is only on screen while paused, so it never blocks Stremio during playback.
   - Hover a word box (150 ms) → TranslationPopup with that word's Reverso Context result.
   - Hover `⇄` → TranslationPopup with the whole line's translation.
   - Clicking the popup pins it; Esc or the popup's ✕ closes it.
4. **Resume.** The cursor leaves the subtitle zone (with 300 ms grace) and no popup is pinned → hide the overlays and `Playback.Resume()`, but only if we paused and Stremio is still foreground. If you click Stremio or press a key while paused, the app forgets that it paused, so it won't toggle playback against you.

Pause and resume work by sending Space, which toggles play/pause in Stremio's player. The overlay windows are no-activate, so Stremio keeps keyboard focus.

## Reverso
Both endpoints are unofficial, so they're isolated behind `ReversoClient`:
- Word: Reverso Context (`context.reverso.net/bst-query-service`), giving translations + example pairs.
- Line: Reverso Translation (`api.reverso.net/translate/v1/translation`), giving the translated text.
- In-memory LRU cache (500 entries) and a 5 s timeout.
- The exact request formats get confirmed against the live sites in the plan's first task and saved as test fixtures.

## Error handling
- Reverso fails or the response can't be parsed → the popup says "Couldn't load translation". The **Open in Reverso** link (`https://context.reverso.net/translation/english-ukrainian/<text>`, opened in the default browser) is in every popup.
- Stremio isn't running or its window isn't found → the app idles; the tray tooltip says "Waiting for Stremio".
- OCR returns nothing or garbage (fewer than 2 letters) → treated as "no subtitle", and the video isn't paused.
- Exceptions inside the watcher are logged to `%LOCALAPPDATA%\StremioReverso\log.txt` and the watcher keeps running.

## Testing
- xUnit: HitTest (word/line/gap/miss, overlapping lines), WordNormalizer (punctuation, `don't`, `well-known`, `♪`, italics artifacts), ReversoParser (fixtures, plus a malformed one).
- Manual check with Stremio playing an English-subtitled title, windowed and fullscreen: hover pauses, word popup, line popup, leaving resumes, no stray pause/resume.

## Known limits
- OCR can misread stylized or very small subtitles. Mitigations: 2x upscale + grayscale, and "Open in Reverso" with the OCR'd text so you can fix it there.
- If Stremio ever stops using Space for play/pause, `Playback` is the one place to change.
