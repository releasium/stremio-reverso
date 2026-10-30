# Stremio Reverso — design

A Chrome/Edge (MV3) extension for **web.stremio.com**. Hovering the subtitles pauses the video and shows a Reverso translation (English → Ukrainian): of the hovered **word**, or of the **whole line**.

## Goals
- Hover on the subtitle area pauses playback; leaving resumes it (only if the extension paused it).
- Hover a word → popup with Reverso Context translations and 2–3 example sentence pairs.
- Hover the line itself (the padding around the words, or a `⇄` handle at the end of the line) → popup with the translation of the whole line.
- Works with Stremio's built-in and addon subtitles as rendered in the web player.

## Non-goals
- The Stremio desktop app (subtitles are drawn by mpv, so they aren't DOM text).
- A settings UI, saved vocabulary, or other language pairs (the pair is one constant: `english-ukrainian`).
- Publishing to the Chrome Web Store (it's loaded unpacked).

## Architecture
Plain JS, no build step.

```
manifest.json          MV3; content script on https://web.stremio.com/*; host_permissions for Reverso
src/content.js         wiring: observer → tokenizer → hover → popup; pause/resume
src/tokenize.js        pure: splits a line into word / non-word segments
src/subtitles.js       finds the subtitle container and wraps cue text in spans (MutationObserver)
src/popup.js           renders and positions the popup (shadow DOM, so Stremio CSS can't leak in)
src/background.js      service worker: Reverso requests and an in-memory LRU cache
src/reverso.js         pure: builds requests and parses responses (also used by the tests)
src/styles.css         word highlight on hover
test/*.test.js         node:test unit tests for tokenize + reverso parsing
```

### Data flow
1. `subtitles.js` watches the player DOM. When cue text appears or changes, it replaces each text node with `<span class="sr-word">` spans (from `tokenize.js`), turns on `pointer-events`, and adds a `⇄` line handle.
2. `content.js`: on `mouseenter` of the subtitle container it pauses the `<video>` and remembers that it did. On `mouseleave` it resumes, unless the popup is pinned.
3. Hovering a word (150 ms debounce) or the line sends `chrome.runtime.sendMessage({type:'word'|'line', text, context})`.
4. `background.js` checks its cache, then calls Reverso:
   - word: Reverso Context (`context.reverso.net` bst-query-service), giving translations + examples
   - line: Reverso Translation (`api.reverso.net/translate/v1/translation`)
5. `popup.js` shows the result just above the subtitle. Click pins the popup; Esc or clicking outside closes it.

## Error handling
- These Reverso endpoints are unofficial. If a request fails or the response can't be parsed, the popup says "Couldn't load translation" and still shows the **Open in Reverso** link (`https://context.reverso.net/translation/english-ukrainian/<text>`), which is present in every popup.
- If the subtitle container isn't found (Stremio changes its DOM), the extension does nothing; it never breaks playback.
- Words are normalized before lookup: lowercased, edge punctuation stripped, and contractions like "don't" kept intact.

## Testing
- `node --test`: tokenizer edge cases (punctuation, contractions, hyphens, `<i>` tags, line breaks) and Reverso response parsing against saved fixtures.
- Manual check in the browser on web.stremio.com with a title that has English subtitles: hover pauses, word popup, line popup, resume.

## Open risk
Stremio's subtitle DOM selectors and the exact Reverso request formats have to be confirmed against the live sites at the start of implementation. That's the plan's first task.
