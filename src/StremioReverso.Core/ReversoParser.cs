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
