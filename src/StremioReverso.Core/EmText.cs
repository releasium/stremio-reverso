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
