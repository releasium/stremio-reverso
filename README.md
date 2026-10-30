# Stremio Reverso

A Windows tray app for the Stremio desktop app. Rest the cursor on a subtitle: Stremio pauses, each word becomes hoverable (Reverso Context EN→UK with examples), and `⇄` at the end of a line translates the whole line. Move the cursor away to resume. Click a popup to pin it; ✕ closes it.

```bash
dotnet run --project src/StremioReverso -c Release
```

Exit from the tray icon. Errors are logged to `%LOCALAPPDATA%\StremioReverso\log.txt`.
