# Stremio Reverso Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows tray app that pauses Stremio when the cursor rests on a subtitle, OCRs the subtitle, and shows Reverso EN→UK translations for the hovered word or the whole line.

**Architecture:** There are two projects plus tests. `StremioReverso.Core` (net8.0) holds all pure logic: geometry, hit testing, word normalization, Reverso parsing and client, and the hover state machine (`HoverLogic`). `StremioReverso` (WPF, net8.0-windows10.0.19041.0) holds the Win32 interop, screen capture and Windows OCR, the overlay windows, and the tray. A 30 Hz `DispatcherTimer` feeds cursor and window state into `HoverLogic`, which returns actions: StartOcr, Pause, Resume, Dismiss.

**Tech Stack:** .NET 8 SDK 8.0.427, WPF + WinForms `NotifyIcon`, `Windows.Media.Ocr` (en-US), AngleSharp 1.1.2, xUnit.

## Global Constraints
- Repo root: `C:\Users\ckkos\Projects\stremio-reverso` (all paths below are relative to it).
- `dotnet` lives at `C:\Program Files\dotnet\dotnet.exe`. In Git Bash, run `export PATH="$PATH:/c/Program Files/dotnet"` first.
- The language pair is the constant `english-ukrainian` (Context page) / `eng`→`ukr` (translate API).
- Subtitle zone: the bottom 35% of the Stremio window. Dwell 250 ms, cooldown 1 s, leave grace 300 ms, hover delay 150 ms, line hit padding 12 px, timer 33 ms.
- Stremio process name: `stremio-shell-ng`. Play/pause is the Space key.
- The overlay windows are `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` and topmost, and must never take focus from Stremio.
- Reverso failures never throw out of `ReversoClient`; they return `null`. Every popup has "Open in Reverso".
- Errors are logged to `%LOCALAPPDATA%\StremioReverso\log.txt`.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: Solution scaffold + Core geometry, hit testing, text helpers

**Files:**
- Create: `.gitignore`, `StremioReverso.sln`
- Create: `src/StremioReverso.Core/StremioReverso.Core.csproj`
- Create: `src/StremioReverso.Core/Geometry.cs`, `HitTest.cs`, `WordNormalizer.cs`, `EmText.cs`
- Create: `tests/StremioReverso.Tests/StremioReverso.Tests.csproj` (fixtures already exist in `tests/StremioReverso.Tests/Fixtures/`)
- Test: `tests/StremioReverso.Tests/TextAndGeometryTests.cs`

**Interfaces:**
- Produces: `Box(double X, double Y, double W, double H)` with `Right`, `Bottom`, `Contains(x, y, pad=0)`, `static Union(IEnumerable<Box>)`, `static SubtitleZone(Box window, double fraction=0.35)`; `OcrWord(string Text, Box Box)`; `OcrLine(string Text, Box Box, IReadOnlyList<OcrWord> Words)`; `HitTest.LineAt(IReadOnlyList<OcrLine>, double x, double y, double pad=12) → OcrLine?`; `WordNormalizer.Normalize(string) → string?`, `WordNormalizer.IsMeaningful(string) → bool`; `EmText.Split(string) → IReadOnlyList<(string Text, bool Em)>`. All in namespace `StremioReverso.Core`.

- [ ] **Step 1: Scaffold**

```bash
cd /c/Users/ckkos/Projects/stremio-reverso
export PATH="$PATH:/c/Program Files/dotnet"
printf 'bin/\nobj/\n.vs/\n*.user\n' > .gitignore
dotnet new sln -n StremioReverso
dotnet new classlib -n StremioReverso.Core -o src/StremioReverso.Core -f net8.0
rm src/StremioReverso.Core/Class1.cs
dotnet add src/StremioReverso.Core package AngleSharp --version 1.1.2
dotnet new xunit -n StremioReverso.Tests -o tests/StremioReverso.Tests -f net8.0
rm tests/StremioReverso.Tests/UnitTest1.cs
dotnet add tests/StremioReverso.Tests reference src/StremioReverso.Core
dotnet sln add src/StremioReverso.Core tests/StremioReverso.Tests
```

Add this to `tests/StremioReverso.Tests/StremioReverso.Tests.csproj`, inside `<Project>`:

```xml
  <ItemGroup>
    <None Include="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing tests** in `tests/StremioReverso.Tests/TextAndGeometryTests.cs`

```csharp
using StremioReverso.Core;

namespace StremioReverso.Tests;

public class TextAndGeometryTests
{
    [Theory]
    [InlineData("Don't,", "don't")]
    [InlineData("don’t", "don't")]
    [InlineData("well-known.", "well-known")]
    [InlineData("‘Hello’", "hello")]
    [InlineData("-Yeah!", "yeah")]
    [InlineData("I", "i")]
    [InlineData("♪", null)]
    [InlineData("123", null)]
    [InlineData("...", null)]
    public void Normalize(string raw, string? expected) =>
        Assert.Equal(expected, WordNormalizer.Normalize(raw));

    [Theory]
    [InlineData("Hi", true)]
    [InlineData("00:12:31", false)]
    [InlineData("♪ a ♪", false)]
    public void IsMeaningful(string text, bool expected) =>
        Assert.Equal(expected, WordNormalizer.IsMeaningful(text));

    [Fact]
    public void EmSplit()
    {
        var parts = EmText.Split("won't <em>bother</em> you");
        Assert.Equal(new[] { ("won't ", false), ("bother", true), (" you", false) }, parts);
    }

    [Fact]
    public void UnionAndZone()
    {
        var u = Box.Union(new[] { new Box(10, 10, 10, 10), new Box(30, 5, 5, 30) });
        Assert.Equal(new Box(10, 5, 25, 30), u);
        Assert.Equal(new Box(0, 650, 1000, 350), Box.SubtitleZone(new Box(0, 0, 1000, 1000)));
    }

    static OcrLine Line(double y) => new("hello there", new Box(100, y, 200, 30), Array.Empty<OcrWord>());

    [Fact]
    public void LineAtPicksNearestLineWithinPadding()
    {
        var lines = new[] { Line(800), Line(840) };
        Assert.Same(lines[1], HitTest.LineAt(lines, 150, 860));
        Assert.Same(lines[0], HitTest.LineAt(lines, 150, 795)); // 5px above, inside 12px pad
        Assert.Null(HitTest.LineAt(lines, 150, 700));
        Assert.Null(HitTest.LineAt(lines, 20, 815));
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test` → Expected: build errors (`Box`, `WordNormalizer` … not found).

- [ ] **Step 4: Implement**

`src/StremioReverso.Core/Geometry.cs`:

```csharp
namespace StremioReverso.Core;

/// Rectangle in screen pixels.
public readonly record struct Box(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;

    public bool Contains(double px, double py, double pad = 0) =>
        px >= X - pad && px <= Right + pad && py >= Y - pad && py <= Bottom + pad;

    public static Box Union(IEnumerable<Box> boxes)
    {
        var list = boxes.ToList();
        if (list.Count == 0) return default;
        double l = list.Min(b => b.X), t = list.Min(b => b.Y);
        double r = list.Max(b => b.Right), btm = list.Max(b => b.Bottom);
        return new Box(l, t, r - l, btm - t);
    }

    /// The bottom part of a window, where subtitles are drawn.
    public static Box SubtitleZone(Box window, double fraction = 0.35) =>
        new(window.X, window.Bottom - window.H * fraction, window.W, window.H * fraction);
}

public sealed record OcrWord(string Text, Box Box);

public sealed record OcrLine(string Text, Box Box, IReadOnlyList<OcrWord> Words);
```

`src/StremioReverso.Core/HitTest.cs`:

```csharp
namespace StremioReverso.Core;

public static class HitTest
{
    /// The OCR line under (or within pad px of) the point; the vertically nearest one wins.
    public static OcrLine? LineAt(IReadOnlyList<OcrLine> lines, double x, double y, double pad = 12) =>
        lines.Where(l => l.Box.Contains(x, y, pad))
             .OrderBy(l => Math.Abs(l.Box.Y + l.Box.H / 2 - y))
             .FirstOrDefault();
}
```

`src/StremioReverso.Core/WordNormalizer.cs`:

```csharp
namespace StremioReverso.Core;

public static class WordNormalizer
{
    /// Lookup form of an OCR'd word: edge punctuation stripped, inner ' and - kept, lowercased.
    /// Null when there is no letter in it.
    public static string? Normalize(string raw)
    {
        var s = raw.Replace('’', '\'').Replace('‘', '\'');
        int start = 0, end = s.Length - 1;
        while (start <= end && !char.IsLetterOrDigit(s[start])) start++;
        while (end >= start && !char.IsLetterOrDigit(s[end])) end--;
        if (start > end) return null;
        var word = s[start..(end + 1)];
        return word.Any(char.IsLetter) ? word.ToLowerInvariant() : null;
    }

    /// OCR output that looks like real subtitle text rather than noise or a timestamp.
    public static bool IsMeaningful(string text) => text.Count(char.IsLetter) >= 2;
}
```

`src/StremioReverso.Core/EmText.cs`:

```csharp
using System.Text.RegularExpressions;

namespace StremioReverso.Core;

public static class EmText
{
    static readonly Regex Em = new("<em>(.*?)</em>", RegexOptions.Singleline);

    /// Splits "a <em>b</em> c" into runs, flagging the highlighted ones.
    public static IReadOnlyList<(string Text, bool Em)> Split(string html)
    {
        var parts = new List<(string, bool)>();
        int pos = 0;
        foreach (Match m in Em.Matches(html))
        {
            if (m.Index > pos) parts.Add((html[pos..m.Index], false));
            parts.Add((m.Groups[1].Value, true));
            pos = m.Index + m.Length;
        }
        if (pos < html.Length) parts.Add((html[pos..], false));
        return parts;
    }
}
```

- [ ] **Step 5: Run tests** — `dotnet test` → Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "Scaffold solution with Core geometry and text helpers

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Reverso parser, cache, client

**Files:**
- Create: `src/StremioReverso.Core/LruCache.cs`, `ReversoParser.cs`, `ReversoClient.cs`
- Test: `tests/StremioReverso.Tests/ReversoTests.cs`
- Fixtures (exist): `Fixtures/context-bother.html` (real Context page for "bother"), `Fixtures/translate-line.json` (real translate response for "I don't want to bother you.")

**Interfaces:**
- Consumes: nothing from Task 1 except the namespace.
- Produces: `Example(string Source, string Target)` (strings may contain `<em>`); `WordResult(IReadOnlyList<string> Translations, IReadOnlyList<Example> Examples)`; `ReversoParser.ParseContextPage(string html, int maxTranslations=6, int maxExamples=3) → WordResult`; `ReversoParser.ParseTranslation(string json) → string?`; `LruCache<TKey,TValue>(int capacity)` with `TryGet`, `Set`; `ReversoClient(HttpClient)`, `static HttpClient CreateHttpClient()`, `static string ContextPageUrl(string text)`, `Task<WordResult?> GetWordAsync(string word, CancellationToken ct = default)`, `Task<string?> TranslateLineAsync(string line, CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing tests** in `tests/StremioReverso.Tests/ReversoTests.cs`

```csharp
using System.Net;
using StremioReverso.Core;

namespace StremioReverso.Tests;

public class ReversoTests
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Calls { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls.Add((r, r.Content is null ? null : await r.Content.ReadAsStringAsync(ct)));
            return respond(r);
        }
    }

    static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [Fact]
    public void ParsesContextPage()
    {
        var r = ReversoParser.ParseContextPage(Fixture("context-bother.html"));
        Assert.Equal("турбувати", r.Translations[0]);
        Assert.Contains("заважати", r.Translations);
        Assert.Equal(6, r.Translations.Count);
        Assert.Equal(3, r.Examples.Count);
        Assert.Equal("The curtain was askew, revealing just enough light to <em>bother</em> her.", r.Examples[0].Source);
        Assert.Equal("Штора була перекошена, пропускаючи достатньо світла, щоб її <em>турбувати</em>.", r.Examples[0].Target);
    }

    [Fact]
    public void ChallengePageParsesToEmpty()
    {
        var r = ReversoParser.ParseContextPage("<html><title>Just a moment...</title></html>");
        Assert.Empty(r.Translations);
        Assert.Empty(r.Examples);
    }

    [Fact]
    public void ParsesTranslation() =>
        Assert.Equal("Я не хочу тебе турбувати.", ReversoParser.ParseTranslation(Fixture("translate-line.json")));

    [Fact]
    public void ContextUrlEscapes() =>
        Assert.Equal("https://context.reverso.net/translation/english-ukrainian/don%27t", ReversoClient.ContextPageUrl("don't"));

    [Fact]
    public void LruEvictsOldest()
    {
        var c = new LruCache<string, int>(2);
        c.Set("a", 1); c.Set("b", 2);
        Assert.True(c.TryGet("a", out _)); // a is now most recent
        c.Set("c", 3);
        Assert.False(c.TryGet("b", out _));
        Assert.True(c.TryGet("a", out var a)); Assert.Equal(1, a);
    }

    [Fact]
    public async Task GetWordParsesAndCaches()
    {
        var h = new FakeHandler(_ => Ok(Fixture("context-bother.html")));
        var client = new ReversoClient(new HttpClient(h));
        var first = await client.GetWordAsync("bother");
        var second = await client.GetWordAsync("bother");
        Assert.Equal("турбувати", first!.Translations[0]);
        Assert.Same(first, second);
        Assert.Single(h.Calls);
        Assert.Equal("https://context.reverso.net/translation/english-ukrainian/bother", h.Calls[0].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetWordReturnsNullOnErrorOrChallenge()
    {
        Assert.Null(await new ReversoClient(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))).GetWordAsync("x"));
        Assert.Null(await new ReversoClient(new HttpClient(new FakeHandler(_ => Ok("<html>Just a moment...</html>")))).GetWordAsync("x"));
    }

    [Fact]
    public async Task TranslateLinePostsAndParses()
    {
        var h = new FakeHandler(_ => Ok(Fixture("translate-line.json")));
        var text = await new ReversoClient(new HttpClient(h)).TranslateLineAsync("I don't want to bother you.");
        Assert.Equal("Я не хочу тебе турбувати.", text);
        var (req, body) = h.Calls.Single();
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://api.reverso.net/translate/v1/translation", req.RequestUri!.ToString());
        Assert.Contains("\"from\":\"eng\"", body);
        Assert.Contains("\"to\":\"ukr\"", body);
        Assert.Contains("\"input\":\"I don\\u0027t want to bother you.\"", body);
    }

    [Fact]
    public async Task TranslateLineReturnsNullOnGarbage() =>
        Assert.Null(await new ReversoClient(new HttpClient(new FakeHandler(_ => Ok("not json")))).TranslateLineAsync("hi"));
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test` → Expected: build errors (`ReversoParser` etc. missing).

- [ ] **Step 3: Implement**

`src/StremioReverso.Core/LruCache.cs`:

```csharp
namespace StremioReverso.Core;

public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    readonly int _capacity;
    readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map = new();
    readonly LinkedList<(TKey Key, TValue Value)> _order = new();
    readonly object _gate = new();

    public LruCache(int capacity) => _capacity = capacity;

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }
    }

    public void Set(TKey key, TValue value)
    {
        lock (_gate)
        {
            if (_map.Remove(key, out var old)) _order.Remove(old);
            _map[key] = _order.AddFirst((key, value));
            if (_map.Count > _capacity)
            {
                _map.Remove(_order.Last!.Value.Key);
                _order.RemoveLast();
            }
        }
    }
}
```

`src/StremioReverso.Core/ReversoParser.cs`:

```csharp
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace StremioReverso.Core;

public sealed record Example(string Source, string Target);

public sealed record WordResult(IReadOnlyList<string> Translations, IReadOnlyList<Example> Examples);

public static class ReversoParser
{
    static readonly Regex NonEmTag = new("<(?!/?em>)[^>]+>");
    static readonly Regex Spaces = new(@"\s+");

    public static WordResult ParseContextPage(string html, int maxTranslations = 6, int maxExamples = 3)
    {
        var doc = new HtmlParser().ParseDocument(html);
        var translations = doc.QuerySelectorAll("#translations-content .display-term")
            .Select(e => e.TextContent.Trim())
            .Where(t => t.Length > 0)
            .Distinct()
            .Take(maxTranslations)
            .ToList();
        var examples = doc.QuerySelectorAll("#examples-content .example:not(.blocked)")
            .Select(e => (Src: e.QuerySelector(".src .text"), Trg: e.QuerySelector(".trg .text")))
            .Where(p => p.Src != null && p.Trg != null)
            .Select(p => new Example(CleanKeepingEm(p.Src!.InnerHtml), CleanKeepingEm(p.Trg!.InnerHtml)))
            .Take(maxExamples)
            .ToList();
        return new WordResult(translations, examples);
    }

    /// Joined "translation" array of the translate API, or null when absent or empty.
    public static string? ParseTranslation(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("translation", out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
        var text = string.Join(" ", arr.EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        return text.Length == 0 ? null : text;
    }

    static string CleanKeepingEm(string innerHtml) =>
        Spaces.Replace(WebUtility.HtmlDecode(NonEmTag.Replace(innerHtml, "")), " ").Trim();
}
```

`src/StremioReverso.Core/ReversoClient.cs`:

```csharp
using System.Text;
using System.Text.Json;

namespace StremioReverso.Core;

/// Unofficial Reverso endpoints. Never throws: any failure is null.
public sealed class ReversoClient
{
    public const string ContextPair = "english-ukrainian";
    public const string TranslateEndpoint = "https://api.reverso.net/translate/v1/translation";
    const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

    readonly HttpClient _http;
    readonly LruCache<string, WordResult> _words = new(500);
    readonly LruCache<string, string> _lines = new(500);

    public ReversoClient(HttpClient http) => _http = http;

    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return http;
    }

    public static string ContextPageUrl(string text) =>
        $"https://context.reverso.net/translation/{ContextPair}/{Uri.EscapeDataString(text)}";

    public async Task<WordResult?> GetWordAsync(string word, CancellationToken ct = default)
    {
        if (_words.TryGet(word, out var cached)) return cached;
        try
        {
            var html = await _http.GetStringAsync(ContextPageUrl(word), ct);
            var result = ReversoParser.ParseContextPage(html);
            if (result.Translations.Count == 0) return null;
            _words.Set(word, result);
            return result;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    public async Task<string?> TranslateLineAsync(string line, CancellationToken ct = default)
    {
        if (_lines.TryGet(line, out var cached)) return cached;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, TranslateEndpoint)
            {
                Content = new StringContent(TranslateBody(line), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("Origin", "https://www.reverso.net");
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var text = ReversoParser.ParseTranslation(await resp.Content.ReadAsStringAsync(ct));
            if (text != null) _lines.Set(line, text);
            return text;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    static string TranslateBody(string text) => JsonSerializer.Serialize(new
    {
        format = "text",
        from = "eng",
        to = "ukr",
        input = text,
        options = new { sentenceSplitter = true, origin = "translation.web", contextResults = false, languageDetection = false },
    });
}
```

- [ ] **Step 4: Run tests** — `dotnet test` → Expected: all PASS. (If `ParsesContextPage` example text differs only in whitespace, adjust `CleanKeepingEm`, not the fixture.)

- [ ] **Step 5: Commit** — `git add -A && git commit -m "Add Reverso parser, LRU cache and client" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 3: HoverLogic state machine

**Files:**
- Create: `src/StremioReverso.Core/HoverLogic.cs`
- Test: `tests/StremioReverso.Tests/HoverLogicTests.cs`

**Interfaces:**
- Consumes: `Box`, `OcrLine`, `HitTest.LineAt`.
- Produces: `enum HoverAction { None, StartOcr, Pause, Resume, Dismiss }`; `HoverLogic` with `OnTick(DateTime now, bool inZone, bool stremioForeground, double x, double y) → HoverAction`, `OnOcrResult(DateTime now, IReadOnlyList<OcrLine> lines, double x, double y) → HoverAction`, `OnOcrFailed(DateTime now)`, `OnUserInput()`, `bool Pinned {get;set;}`, `bool IsPaused`, `bool PausedByUs`.
- `inZone` semantics (for the caller): true when the cursor is over our overlay or popup, or when Stremio is foreground and the cursor is in its subtitle zone.

- [ ] **Step 1: Write the failing tests** in `tests/StremioReverso.Tests/HoverLogicTests.cs`

```csharp
using StremioReverso.Core;

namespace StremioReverso.Tests;

public class HoverLogicTests
{
    static readonly DateTime T0 = new(2026, 1, 1);
    static DateTime Ms(int ms) => T0.AddMilliseconds(ms);
    static readonly OcrLine[] Lines = { new("hello there", new Box(100, 800, 200, 30), Array.Empty<OcrWord>()) };

    static HoverLogic Paused()
    {
        var h = new HoverLogic();
        h.OnTick(Ms(0), true, true, 150, 815);
        Assert.Equal(HoverAction.StartOcr, h.OnTick(Ms(260), true, true, 150, 815));
        Assert.Equal(HoverAction.Pause, h.OnOcrResult(Ms(400), Lines, 150, 815));
        return h;
    }

    [Fact]
    public void DwellStartsOcr()
    {
        var h = new HoverLogic();
        Assert.Equal(HoverAction.None, h.OnTick(Ms(0), true, true, 150, 815));
        Assert.Equal(HoverAction.None, h.OnTick(Ms(100), true, true, 151, 815));
        Assert.Equal(HoverAction.StartOcr, h.OnTick(Ms(260), true, true, 152, 816));
        Assert.Equal(HoverAction.None, h.OnTick(Ms(300), true, true, 152, 816)); // waiting for OCR
    }

    [Fact]
    public void MovingRestartsDwell()
    {
        var h = new HoverLogic();
        h.OnTick(Ms(0), true, true, 150, 815);
        h.OnTick(Ms(200), true, true, 180, 815);
        Assert.Equal(HoverAction.None, h.OnTick(Ms(300), true, true, 180, 815));
        Assert.Equal(HoverAction.StartOcr, h.OnTick(Ms(460), true, true, 180, 815));
    }

    [Fact]
    public void OutOfZoneDoesNothing()
    {
        var h = new HoverLogic();
        h.OnTick(Ms(0), false, true, 150, 100);
        Assert.Equal(HoverAction.None, h.OnTick(Ms(500), false, true, 150, 100));
    }

    [Fact]
    public void MissThenCooldown()
    {
        var h = new HoverLogic();
        h.OnTick(Ms(0), true, true, 150, 700);
        h.OnTick(Ms(260), true, true, 150, 700);
        Assert.Equal(HoverAction.None, h.OnOcrResult(Ms(400), Lines, 150, 700));
        Assert.False(h.IsPaused);
        h.OnTick(Ms(500), true, true, 150, 700);
        Assert.Equal(HoverAction.None, h.OnTick(Ms(1300), true, true, 150, 700)); // still cooling down until 1400
        h.OnTick(Ms(1450), true, true, 150, 700);
        Assert.Equal(HoverAction.StartOcr, h.OnTick(Ms(1710), true, true, 150, 700));
    }

    [Fact]
    public void OcrFailureReturnsToIdle()
    {
        var h = new HoverLogic();
        h.OnTick(Ms(0), true, true, 150, 815);
        h.OnTick(Ms(260), true, true, 150, 815);
        h.OnOcrFailed(Ms(300));
        Assert.Equal(HoverAction.None, h.OnOcrResult(Ms(310), Lines, 150, 815));
    }

    [Fact]
    public void PauseThenLeaveResumesAfterGrace()
    {
        var h = Paused();
        Assert.True(h.IsPaused && h.PausedByUs);
        Assert.Equal(HoverAction.None, h.OnTick(Ms(500), true, true, 150, 815));
        Assert.Equal(HoverAction.None, h.OnTick(Ms(600), false, true, 150, 100));
        Assert.Equal(HoverAction.None, h.OnTick(Ms(800), false, true, 150, 100));
        Assert.Equal(HoverAction.Resume, h.OnTick(Ms(910), false, true, 150, 100));
        Assert.False(h.IsPaused);
    }

    [Fact]
    public void ComingBackWithinGraceStaysPaused()
    {
        var h = Paused();
        h.OnTick(Ms(600), false, true, 150, 100);
        h.OnTick(Ms(700), true, true, 150, 815);
        Assert.Equal(HoverAction.None, h.OnTick(Ms(1000), false, true, 150, 100));
        Assert.Equal(HoverAction.None, h.OnTick(Ms(1200), false, true, 150, 100));
        Assert.Equal(HoverAction.Resume, h.OnTick(Ms(1300), false, true, 150, 100));
    }

    [Fact]
    public void PinnedNeverResumes()
    {
        var h = Paused();
        h.Pinned = true;
        h.OnTick(Ms(600), false, true, 150, 100);
        Assert.Equal(HoverAction.None, h.OnTick(Ms(2000), false, true, 150, 100));
        h.Pinned = false;
        h.OnTick(Ms(2100), false, true, 150, 100);
        Assert.Equal(HoverAction.Resume, h.OnTick(Ms(2500), false, true, 150, 100));
    }

    [Fact]
    public void UserInputMeansDismissNotResume()
    {
        var h = Paused();
        h.OnUserInput();
        h.OnTick(Ms(600), false, true, 150, 100);
        Assert.Equal(HoverAction.Dismiss, h.OnTick(Ms(1000), false, true, 150, 100));
    }

    [Fact]
    public void StremioLostFocusMeansDismiss()
    {
        var h = Paused();
        h.OnTick(Ms(600), false, false, 150, 100);
        Assert.Equal(HoverAction.Dismiss, h.OnTick(Ms(1000), false, false, 150, 100));
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test` → Expected: build errors (`HoverLogic` missing).

- [ ] **Step 3: Implement** `src/StremioReverso.Core/HoverLogic.cs`

```csharp
namespace StremioReverso.Core;

public enum HoverAction { None, StartOcr, Pause, Resume, Dismiss }

/// Decides when to OCR, pause, and resume. Pure: the caller feeds it time, cursor and window state.
public sealed class HoverLogic
{
    public static readonly TimeSpan Dwell = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan LeaveGrace = TimeSpan.FromMilliseconds(300);
    public const double StillTolerance = 4;

    enum Phase { Idle, Dwelling, Recognizing, Paused }

    Phase _phase = Phase.Idle;
    double _lastX, _lastY;
    DateTime _stillSince, _cooldownUntil, _leftAt;
    bool _left;

    public bool PausedByUs { get; private set; }
    public bool Pinned { get; set; }
    public bool IsPaused => _phase == Phase.Paused;

    public HoverAction OnTick(DateTime now, bool inZone, bool stremioForeground, double x, double y)
    {
        switch (_phase)
        {
            case Phase.Paused:
                if (inZone || Pinned) { _left = false; return HoverAction.None; }
                if (!_left) { _left = true; _leftAt = now; }
                if (now - _leftAt < LeaveGrace) return HoverAction.None;
                var resume = PausedByUs && stremioForeground;
                Reset(now, cooldown: false);
                return resume ? HoverAction.Resume : HoverAction.Dismiss;

            case Phase.Recognizing:
                return HoverAction.None;

            default:
                if (!inZone || now < _cooldownUntil) { _phase = Phase.Idle; return HoverAction.None; }
                if (_phase == Phase.Idle || Math.Abs(x - _lastX) > StillTolerance || Math.Abs(y - _lastY) > StillTolerance)
                {
                    _phase = Phase.Dwelling;
                    _stillSince = now;
                    _lastX = x;
                    _lastY = y;
                    return HoverAction.None;
                }
                if (now - _stillSince < Dwell) return HoverAction.None;
                _phase = Phase.Recognizing;
                return HoverAction.StartOcr;
        }
    }

    public HoverAction OnOcrResult(DateTime now, IReadOnlyList<OcrLine> lines, double x, double y)
    {
        if (_phase != Phase.Recognizing) return HoverAction.None;
        if (HitTest.LineAt(lines, x, y) is null) { Reset(now, cooldown: true); return HoverAction.None; }
        _phase = Phase.Paused;
        PausedByUs = true;
        _left = false;
        return HoverAction.Pause;
    }

    public void OnOcrFailed(DateTime now)
    {
        if (_phase == Phase.Recognizing) Reset(now, cooldown: true);
    }

    /// The user clicked or pressed a key in Stremio while we were paused, so playback is theirs again.
    public void OnUserInput()
    {
        if (_phase == Phase.Paused) PausedByUs = false;
    }

    void Reset(DateTime now, bool cooldown)
    {
        _phase = Phase.Idle;
        PausedByUs = false;
        Pinned = false;
        _left = false;
        if (cooldown) _cooldownUntil = now + Cooldown;
    }
}
```

- [ ] **Step 4: Run tests** — `dotnet test` → Expected: all PASS.

- [ ] **Step 5: Commit** — `git add -A && git commit -m "Add HoverLogic state machine" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`

---

### Task 4: Windows app — interop, OCR, overlay, popup, watcher, tray

There's no unit test for this task; it's all OS glue. Its gates are a clean build plus the manual check in Step 9.

**Files:**
- Create: `src/StremioReverso/StremioReverso.csproj`, `app.manifest`, `App.xaml`, `App.xaml.cs`, `Log.cs`
- Create: `src/StremioReverso/Win32/Native.cs`, `src/StremioReverso/Stremio/StremioWindow.cs`, `src/StremioReverso/Stremio/Playback.cs`
- Create: `src/StremioReverso/Ocr/SubtitleOcr.cs`
- Create: `src/StremioReverso/UI/WindowPlacement.cs`, `UI/WordOverlay.xaml(.cs)`, `UI/TranslationPopup.xaml(.cs)`
- Create: `src/StremioReverso/HoverWatcher.cs`
- Create: `README.md`

**Interfaces:**
- Consumes: everything from Tasks 1–3.
- Produces: the runnable `StremioReverso.exe`.

- [ ] **Step 1: Project files**

```bash
mkdir -p src/StremioReverso/{Win32,Stremio,Ocr,UI}
```

`src/StremioReverso/StremioReverso.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <Using Remove="System.Windows.Forms" />
    <Using Remove="System.Drawing" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\StremioReverso.Core\StremioReverso.Core.csproj" />
  </ItemGroup>
</Project>
```

`src/StremioReverso/app.manifest` (per-monitor DPI aware, so cursor, capture and window positions all use physical pixels):

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="StremioReverso.app"/>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/PM</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

```bash
dotnet sln add src/StremioReverso
```

- [ ] **Step 2: Interop + Stremio + Log**

`src/StremioReverso/Win32/Native.cs`:

```csharp
using System.Runtime.InteropServices;
using StremioReverso.Core;

namespace StremioReverso.Win32;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    public const uint GA_ROOT = 2;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_SPACE = 0x20;
    public const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
    public const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10;
    public const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 2;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint count, INPUT[] inputs, int size);

    public static Box ToBox(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    public static bool IsDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;
}
```

`src/StremioReverso/Stremio/StremioWindow.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using StremioReverso.Core;
using StremioReverso.Win32;

namespace StremioReverso.Stremio;

/// The Stremio 5 desktop window (stremio-shell-ng.exe).
internal sealed class StremioWindow
{
    const string ProcessName = "stremio-shell-ng";
    IntPtr _hwnd;
    DateTime _nextLookup;

    IntPtr Handle
    {
        get
        {
            if (_hwnd != IntPtr.Zero && Native.IsWindow(_hwnd)) return _hwnd;
            if (DateTime.UtcNow < _nextLookup) return IntPtr.Zero;
            _nextLookup = DateTime.UtcNow.AddSeconds(2);
            _hwnd = IntPtr.Zero;
            foreach (var p in Process.GetProcessesByName(ProcessName))
                using (p) if (_hwnd == IntPtr.Zero) _hwnd = p.MainWindowHandle;
            return _hwnd;
        }
    }

    public Box? Bounds =>
        Handle != IntPtr.Zero &&
        Native.DwmGetWindowAttribute(_hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<Native.RECT>()) == 0
            ? Native.ToBox(r)
            : null;

    public bool IsForeground =>
        Handle != IntPtr.Zero && Native.GetAncestor(Native.GetForegroundWindow(), Native.GA_ROOT) == _hwnd;
}
```

`src/StremioReverso/Stremio/Playback.cs`:

```csharp
using System.Runtime.InteropServices;
using StremioReverso.Win32;

namespace StremioReverso.Stremio;

/// Stremio's player toggles play/pause on Space.
internal static class Playback
{
    public static void TogglePlayPause()
    {
        var inputs = new[] { Key(0), Key(Native.KEYEVENTF_KEYUP) };
        Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.INPUT>());
    }

    static Native.INPUT Key(uint flags) => new()
    {
        type = Native.INPUT_KEYBOARD,
        u = new Native.InputUnion { ki = new Native.KEYBDINPUT { wVk = Native.VK_SPACE, dwFlags = flags } },
    };
}
```

`src/StremioReverso/Log.cs`:

```csharp
using System.IO;

namespace StremioReverso;

internal static class Log
{
    static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StremioReverso", "log.txt");

    public static void Error(string where, Exception e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:s} {where}: {e}\n");
        }
        catch { /* logging must never crash the app */ }
    }
}
```

- [ ] **Step 3: OCR** — `src/StremioReverso/Ocr/SubtitleOcr.cs`

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using StremioReverso.Core;
using WinOcr = Windows.Media.Ocr;

namespace StremioReverso.Ocr;

internal sealed class SubtitleOcr
{
    readonly WinOcr.OcrEngine _engine = WinOcr.OcrEngine.TryCreateFromLanguage(new Language("en-US"))
        ?? throw new InvalidOperationException("English (en-US) OCR is not installed");

    /// OCRs a screen rectangle (physical pixels) and returns subtitle-looking lines in screen coordinates.
    public async Task<IReadOnlyList<OcrLine>> RecognizeAsync(Box region)
    {
        int w = (int)region.W, h = (int)region.H;
        if (w <= 0 || h <= 0) return Array.Empty<OcrLine>();
        double scale = Math.Min(2.0, WinOcr.OcrEngine.MaxImageDimension / (double)Math.Max(w, h));
        int sw = (int)(w * scale), sh = (int)(h * scale);

        using var shot = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(shot))
            g.CopyFromScreen((int)region.X, (int)region.Y, 0, 0, new Size(w, h));
        using var scaled = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(shot, 0, 0, sw, sh);
        }

        using var ms = new MemoryStream();
        scaled.Save(ms, ImageFormat.Bmp);
        ms.Position = 0;
        var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await _engine.RecognizeAsync(bitmap);
        return result.Lines
            .Select(l => ToLine(l, region, scale))
            .Where(l => WordNormalizer.IsMeaningful(l.Text))
            .ToList();
    }

    static OcrLine ToLine(WinOcr.OcrLine line, Box region, double scale)
    {
        var words = line.Words.Select(w => new OcrWord(w.Text, new Box(
            region.X + w.BoundingRect.X / scale,
            region.Y + w.BoundingRect.Y / scale,
            w.BoundingRect.Width / scale,
            w.BoundingRect.Height / scale))).ToList();
        return new OcrLine(line.Text, Box.Union(words.Select(w => w.Box)), words);
    }
}
```

- [ ] **Step 4: Window helpers + overlay** — `src/StremioReverso/UI/WindowPlacement.cs`

```csharp
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using StremioReverso.Core;
using StremioReverso.Win32;

namespace StremioReverso.UI;

/// Positions WPF windows in physical pixels and keeps them from ever taking focus.
internal static class WindowPlacement
{
    public static void MakeOverlay(Window w) => w.SourceInitialized += (_, _) =>
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE,
            Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
    };

    public static void Place(Window w, Box px)
    {
        if (!w.IsVisible) w.Show();
        Native.SetWindowPos(Hwnd(w), Native.HWND_TOPMOST, (int)px.X, (int)px.Y, (int)px.W, (int)px.H, Native.SWP_NOACTIVATE);
    }

    public static void Move(Window w, double x, double y) =>
        Native.SetWindowPos(Hwnd(w), Native.HWND_TOPMOST, (int)x, (int)y, 0, 0, Native.SWP_NOACTIVATE | Native.SWP_NOSIZE);

    public static double Scale(Window w) => VisualTreeHelper.GetDpi(w).DpiScaleX;

    public static bool ContainsCursor(Window w, double x, double y) =>
        w.IsVisible && Native.GetWindowRect(Hwnd(w), out var r) && Native.ToBox(r).Contains(x, y);

    static IntPtr Hwnd(Window w) => new WindowInteropHelper(w).EnsureHandle();
}
```

`src/StremioReverso/UI/WordOverlay.xaml`:

```xml
<Window x:Class="StremioReverso.UI.WordOverlay"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        ShowInTaskbar="False" ShowActivated="False" Topmost="True" ResizeMode="NoResize"
        Left="-10000" Top="-10000" Width="10" Height="10">
    <Canvas x:Name="Root" />
</Window>
```

`src/StremioReverso/UI/WordOverlay.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StremioReverso.Core;

namespace StremioReverso.UI;

/// Sits over the frozen subtitle while paused: one hover target per word, and a ⇄ handle per line.
public partial class WordOverlay : Window
{
    public event Action<OcrLine, OcrWord>? WordHovered;
    public event Action<OcrLine>? LineHovered;

    readonly DispatcherTimer _hoverDelay = new() { Interval = TimeSpan.FromMilliseconds(150) };
    Action? _pending;

    public WordOverlay()
    {
        InitializeComponent();
        WindowPlacement.MakeOverlay(this);
        _hoverDelay.Tick += (_, _) => { _hoverDelay.Stop(); _pending?.Invoke(); };
    }

    public void ShowLines(IReadOnlyList<OcrLine> lines)
    {
        var area = Box.Union(lines.Select(l => new Box(l.Box.X - 6, l.Box.Y - 6, l.Box.W + 48, l.Box.H + 12)));
        WindowPlacement.Place(this, area);
        double s = WindowPlacement.Scale(this);
        Root.Children.Clear();
        foreach (var line in lines)
        {
            foreach (var word in line.Words)
            {
                if (WordNormalizer.Normalize(word.Text) is null) continue;
                AddTarget(word.Box, area, s, null, () => WordHovered?.Invoke(line, word));
            }
            var handle = new Box(line.Box.Right + 8, line.Box.Y + line.Box.H / 2 - 12, 28, 24);
            AddTarget(handle, area, s, "⇄", () => LineHovered?.Invoke(line));
        }
    }

    public new void Hide()
    {
        _hoverDelay.Stop();
        _pending = null;
        base.Hide();
    }

    void AddTarget(Box box, Box area, double s, string? label, Action onHover)
    {
        var border = new Border
        {
            Width = box.W / s + 4,
            Height = box.H / s + 4,
            // Alpha 1 keeps the box hit-testable while staying invisible.
            Background = new SolidColorBrush(Color.FromArgb(label is null ? (byte)1 : (byte)180, 20, 20, 20)),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(3),
            Cursor = Cursors.Hand,
        };
        if (label != null)
            border.Child = new TextBlock
            {
                Text = label, Foreground = Brushes.White, FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        border.MouseEnter += (_, _) =>
        {
            border.BorderBrush = Brushes.Gold;
            _pending = onHover;
            _hoverDelay.Stop();
            _hoverDelay.Start();
        };
        border.MouseLeave += (_, _) =>
        {
            border.BorderBrush = Brushes.Transparent;
            if (_pending == onHover) _hoverDelay.Stop();
        };
        Canvas.SetLeft(border, (box.X - area.X) / s - 2);
        Canvas.SetTop(border, (box.Y - area.Y) / s - 2);
        Root.Children.Add(border);
    }
}
```

- [ ] **Step 5: Popup**

`src/StremioReverso/UI/TranslationPopup.xaml`:

```xml
<Window x:Class="StremioReverso.UI.TranslationPopup"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        ShowInTaskbar="False" ShowActivated="False" Topmost="True" ResizeMode="NoResize"
        SizeToContent="WidthAndHeight" Left="-10000" Top="-10000">
    <Border Background="#F0202124" BorderBrush="#555" BorderThickness="1" CornerRadius="8"
            Padding="12" MaxWidth="460" MinWidth="160" MouseLeftButtonDown="OnBodyClick">
        <StackPanel>
            <DockPanel>
                <Button DockPanel.Dock="Right" Content="✕" Click="OnClose" Background="Transparent"
                        BorderThickness="0" Foreground="White" Padding="6,0" Cursor="Hand" />
                <TextBlock x:Name="PinMark" DockPanel.Dock="Right" Text="📌" Visibility="Collapsed"
                           Margin="0,0,6,0" Foreground="White" />
                <TextBlock x:Name="Heading" Foreground="White" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap" />
            </DockPanel>
            <TextBlock x:Name="Translations" Foreground="#FFD54F" FontSize="15" TextWrapping="Wrap" Margin="0,6,0,0" />
            <StackPanel x:Name="Examples" Margin="0,4,0,0" />
            <TextBlock Margin="0,8,0,0">
                <Hyperlink Foreground="#8AB4F8" Click="OnOpenReverso">Open in Reverso</Hyperlink>
            </TextBlock>
        </StackPanel>
    </Border>
</Window>
```

`src/StremioReverso/UI/TranslationPopup.xaml.cs`:

```csharp
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using StremioReverso.Core;

namespace StremioReverso.UI;

public partial class TranslationPopup : Window
{
    const string Failed = "Couldn't load translation";
    static readonly Brush SourceBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
    static readonly Brush TargetBrush = new SolidColorBrush(Color.FromRgb(0xA5, 0xD6, 0xA7));

    public event Action<bool>? PinnedChanged;
    string _query = "";

    public TranslationPopup()
    {
        InitializeComponent();
        WindowPlacement.MakeOverlay(this);
    }

    public void ShowLoading(string heading, Box anchor) => Fill(heading, "…", anchor);

    public void ShowWord(string word, WordResult? result, Box anchor)
    {
        Fill(word, result is null ? Failed : string.Join(", ", result.Translations), anchor, () =>
        {
            foreach (var ex in result?.Examples ?? Array.Empty<Example>())
            {
                Examples.Children.Add(Rich(ex.Source, SourceBrush, 8));
                Examples.Children.Add(Rich(ex.Target, TargetBrush, 0));
            }
        });
    }

    public void ShowLine(string line, string? translation, Box anchor) => Fill(line, translation ?? Failed, anchor);

    public void HidePopup()
    {
        PinMark.Visibility = Visibility.Collapsed;
        Hide();
    }

    void Fill(string heading, string translations, Box anchor, Action? addExamples = null)
    {
        _query = heading;
        Heading.Text = heading;
        Translations.Text = translations;
        Examples.Children.Clear();
        addExamples?.Invoke();
        Present(anchor);
    }

    void Present(Box anchor)
    {
        if (!IsVisible) Show();
        UpdateLayout();
        double s = WindowPlacement.Scale(this);
        double w = ActualWidth * s, h = ActualHeight * s;
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)anchor.X, (int)anchor.Y)).WorkingArea;
        double x = Math.Clamp(anchor.X + anchor.W / 2 - w / 2, screen.Left, Math.Max(screen.Left, screen.Right - w));
        double y = anchor.Y - h - 10;
        if (y < screen.Top) y = anchor.Bottom + 10;
        WindowPlacement.Move(this, x, y);
    }

    static TextBlock Rich(string html, Brush brush, double top)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = brush, FontSize = 13, Margin = new Thickness(0, top, 0, 0) };
        foreach (var (text, em) in EmText.Split(html))
            tb.Inlines.Add(em ? new Bold(new Run(text)) : new Run(text));
        return tb;
    }

    void OnBodyClick(object sender, MouseButtonEventArgs e)
    {
        PinMark.Visibility = Visibility.Visible;
        PinnedChanged?.Invoke(true);
    }

    void OnClose(object sender, RoutedEventArgs e)
    {
        HidePopup();
        PinnedChanged?.Invoke(false);
    }

    void OnOpenReverso(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(ReversoClient.ContextPageUrl(_query)) { UseShellExecute = true });
}
```

- [ ] **Step 6: Watcher** — `src/StremioReverso/HoverWatcher.cs`

```csharp
using System.Windows.Threading;
using StremioReverso.Core;
using StremioReverso.Ocr;
using StremioReverso.Stremio;
using StremioReverso.UI;
using StremioReverso.Win32;

namespace StremioReverso;

/// Glues the OS to HoverLogic: polls cursor/window 30×/s and carries out the actions it returns.
internal sealed class HoverWatcher
{
    readonly StremioWindow _stremio = new();
    readonly SubtitleOcr _ocr = new();
    readonly ReversoClient _reverso = new(ReversoClient.CreateHttpClient());
    readonly HoverLogic _logic = new();
    readonly WordOverlay _overlay = new();
    readonly TranslationPopup _popup = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    DateTime _ignoreInputUntil;
    CancellationTokenSource? _lookup;
    string _status = "";

    public event Action<string>? StatusChanged;

    public HoverWatcher()
    {
        _timer.Tick += (_, _) =>
        {
            try { Tick(); }
            catch (Exception e) { Log.Error("tick", e); }
        };
        _overlay.WordHovered += OnWordHovered;
        _overlay.LineHovered += OnLineHovered;
        _popup.PinnedChanged += pinned => _logic.Pinned = pinned;
    }

    public void Start() => _timer.Start();

    void Tick()
    {
        var now = DateTime.UtcNow;
        Native.GetCursorPos(out var p);
        var bounds = _stremio.Bounds;
        SetStatus(bounds is null ? "Stremio Reverso: waiting for Stremio" : "Stremio Reverso: hover subtitles");

        bool foreground = bounds != null && _stremio.IsForeground;
        bool overUi = WindowPlacement.ContainsCursor(_overlay, p.X, p.Y) || WindowPlacement.ContainsCursor(_popup, p.X, p.Y);
        bool inZone = overUi || (foreground && Box.SubtitleZone(bounds!.Value).Contains(p.X, p.Y));

        if (_logic.IsPaused && !overUi && now > _ignoreInputUntil &&
            (Native.IsDown(Native.VK_LBUTTON) || Native.IsDown(Native.VK_RBUTTON) || Native.IsDown(Native.VK_SPACE)))
            _logic.OnUserInput();

        switch (_logic.OnTick(now, inZone, foreground, p.X, p.Y))
        {
            case HoverAction.StartOcr:
                if (bounds is { } b) _ = RecognizeAsync(Box.SubtitleZone(b));
                else _logic.OnOcrFailed(now);
                break;
            case HoverAction.Resume:
                HideUi();
                TogglePlayback();
                break;
            case HoverAction.Dismiss:
                HideUi();
                break;
        }
    }

    async Task RecognizeAsync(Box zone)
    {
        try
        {
            var lines = await _ocr.RecognizeAsync(zone);
            Native.GetCursorPos(out var p);
            if (_logic.OnOcrResult(DateTime.UtcNow, lines, p.X, p.Y) != HoverAction.Pause) return;
            TogglePlayback();
            await Task.Delay(150); // let the paused frame settle, then read it cleanly
            var frozen = await _ocr.RecognizeAsync(zone);
            if (_logic.IsPaused) _overlay.ShowLines(frozen.Count > 0 ? frozen : lines);
        }
        catch (Exception e)
        {
            Log.Error("ocr", e);
            _logic.OnOcrFailed(DateTime.UtcNow);
        }
    }

    async void OnWordHovered(OcrLine line, OcrWord word)
    {
        try
        {
            var query = WordNormalizer.Normalize(word.Text);
            if (query is null) return;
            var ct = NewLookup();
            _popup.ShowLoading(query, word.Box);
            var result = await _reverso.GetWordAsync(query, ct);
            if (!ct.IsCancellationRequested) _popup.ShowWord(query, result, word.Box);
        }
        catch (Exception e) { Log.Error("word", e); }
    }

    async void OnLineHovered(OcrLine line)
    {
        try
        {
            var ct = NewLookup();
            _popup.ShowLoading(line.Text, line.Box);
            var result = await _reverso.TranslateLineAsync(line.Text, ct);
            if (!ct.IsCancellationRequested) _popup.ShowLine(line.Text, result, line.Box);
        }
        catch (Exception e) { Log.Error("line", e); }
    }

    CancellationToken NewLookup()
    {
        _lookup?.Cancel();
        _lookup = new CancellationTokenSource();
        return _lookup.Token;
    }

    void HideUi()
    {
        _lookup?.Cancel();
        _overlay.Hide();
        _popup.HidePopup();
    }

    void TogglePlayback()
    {
        _ignoreInputUntil = DateTime.UtcNow.AddMilliseconds(300);
        Playback.TogglePlayPause();
    }

    void SetStatus(string status)
    {
        if (status == _status) return;
        _status = status;
        StatusChanged?.Invoke(status);
    }
}
```

- [ ] **Step 7: App + tray**

`src/StremioReverso/App.xaml`:

```xml
<Application x:Class="StremioReverso.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown" />
```

`src/StremioReverso/App.xaml.cs`:

```csharp
using System.Windows;
using Forms = System.Windows.Forms;

namespace StremioReverso;

public partial class App : Application
{
    Forms.NotifyIcon? _tray;
    Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _single = new Mutex(true, "StremioReverso.SingleInstance", out bool first);
        if (!first) { Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => { Log.Error("unhandled", args.Exception); args.Handled = true; };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => Shutdown());
        _tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Information,
            Text = "Stremio Reverso",
            Visible = true,
            ContextMenuStrip = menu,
        };

        var watcher = new HoverWatcher();
        watcher.StatusChanged += s => _tray.Text = s.Length > 63 ? s[..63] : s;
        watcher.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _single?.Dispose();
        base.OnExit(e);
    }
}
```

`README.md`:

````markdown
# Stremio Reverso

A Windows tray app for the Stremio desktop app. Rest the cursor on a subtitle: Stremio pauses, each word becomes hoverable (Reverso Context EN→UK with examples), and `⇄` at the end of a line translates the whole line. Move the cursor away to resume. Click a popup to pin it; ✕ closes it.

```bash
dotnet run --project src/StremioReverso -c Release
```

Exit from the tray icon. Errors are logged to `%LOCALAPPDATA%\StremioReverso\log.txt`.
````

- [ ] **Step 8: Build + tests**

Run: `dotnet build -c Release && dotnet test` → Expected: 0 errors, all tests PASS.

- [ ] **Step 9: Manual check** (with the user)
1. Start the app (`dotnet run --project src/StremioReverso -c Release`); the tray icon appears and its tooltip says "waiting for Stremio" until Stremio is open.
2. Play an English-subtitled title in Stremio, windowed. Rest the cursor on a subtitle → it pauses within ~0.5 s, and gold underlines appear when hovering words.
3. Hover a word → the popup shows translations + examples. Hover `⇄` → the line translation.
4. Click the popup → 📌; moving away doesn't resume. ✕ → it resumes after you leave.
5. Move away without pinning → it resumes. Hovering the seek bar / empty video area → no pause.
6. Repeat in fullscreen.
7. Pause manually with Space, then hover a subtitle and leave → the video stays paused. (The app sends Space only when it paused.)
   - Known gap: if the video is already paused by the user and the cursor rests on a subtitle, the app will send Space, which *plays* it. If this happens in practice, add a check (pixel diff of two frames 100 ms apart = playing) before pausing.

- [ ] **Step 10: Commit** — `git add -A && git commit -m "Add Windows tray app: OCR overlay, Reverso popup, hover watcher" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`
