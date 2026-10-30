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
