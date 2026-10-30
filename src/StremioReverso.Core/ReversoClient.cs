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
