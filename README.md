# Stremio Reverso

A Windows tray app for the Stremio desktop app. Hover a subtitle and Stremio pauses at once. Each word becomes hoverable and opens its Reverso Context page (EN→UK), and `⇄` at the end of a line opens the Reverso translation of the whole line. Click a word, or drag across several, to translate the selection (up to 3 words → Reverso Context, longer → full translation). Move the cursor away to resume. 📌 keeps the popup open; ✕ closes it; ↗ opens the page in your browser.

```bash
dotnet run --project src/StremioReverso -c Release
```

First run: tray icon → **Sign in to Reverso…**, log in, then close the window. The popup uses the same login (it's stored in `%LOCALAPPDATA%\StremioReverso\WebView2`).

Exit from the tray icon. Errors are logged to `%LOCALAPPDATA%\StremioReverso\log.txt`.
