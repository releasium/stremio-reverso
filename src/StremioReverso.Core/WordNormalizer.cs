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
