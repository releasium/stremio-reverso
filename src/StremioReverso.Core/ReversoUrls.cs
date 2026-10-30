namespace StremioReverso.Core;

/// Reverso pages for the popup's browser (EN → UK).
public static class ReversoUrls
{
    public const string SignIn = "https://context.reverso.net/";

    public static string ContextPage(string word) =>
        $"https://context.reverso.net/translation/english-ukrainian/{Uri.EscapeDataString(word)}";

    public static string TranslationPage(string line) =>
        $"https://www.reverso.net/text-translation#sl=eng&tl=ukr&text={Uri.EscapeDataString(line)}";
}
