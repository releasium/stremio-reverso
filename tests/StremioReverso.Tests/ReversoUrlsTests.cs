using StremioReverso.Core;

namespace StremioReverso.Tests;

public class ReversoUrlsTests
{
    [Fact]
    public void ContextPageEscapesWord() =>
        Assert.Equal("https://context.reverso.net/translation/english-ukrainian/don%27t", ReversoUrls.ContextPage("don't"));

    [Fact]
    public void TranslationPageCarriesLanguagesAndText() =>
        Assert.Equal("https://www.reverso.net/text-translation#sl=eng&tl=ukr&text=I%20don%27t%20know.",
            ReversoUrls.TranslationPage("I don't know."));
}
