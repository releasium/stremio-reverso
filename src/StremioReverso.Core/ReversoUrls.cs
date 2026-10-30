namespace StremioReverso.Core;

/// Reverso pages for the popup's browser (EN → UK).
public static class ReversoUrls
{
    public const string SignIn = "https://context.reverso.net/";

    public static string ContextPage(string word) =>
        $"https://context.reverso.net/translation/english-ukrainian/{Uri.EscapeDataString(word)}";

    public const int MaxContextWords = 3;

    /// Selected words: short phrases go to Reverso Context, longer ones to full translation.
    public static string ForPhrase(string phrase)
    {
        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= MaxContextWords && WordNormalizer.Normalize(phrase) is { } normalized
            ? ContextPage(normalized)
            : TranslationPage(string.Join(' ', words));
    }

    public static string TranslationPage(string line) =>
        $"https://www.reverso.net/text-translation#sl=eng&tl=ukr&text={Uri.EscapeDataString(line)}";
}
