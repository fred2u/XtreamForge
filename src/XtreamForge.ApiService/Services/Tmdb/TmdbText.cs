using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace XtreamForge.ApiService.Services.Tmdb;

public static partial class TmdbText
{
    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlphanumeric();

    // "and" / "et" are dropped so that "Fast and Furious" and "Fast & Furious" normalize identically
    [GeneratedRegex(@"\b(?:et|and)\b")]
    private static partial Regex Conjunction();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpace();

    /// <summary>
    /// Lowercases, removes diacritics, punctuation and conjunctions so that texts coming from different sources can be compared.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        {
            builder.Append(character);
        }

        var text = NonAlphanumeric().Replace(builder.ToString(), " ");
        text = Conjunction().Replace(text, " ");

        return MultiSpace().Replace(text, " ").Trim();
    }

    /// <summary>
    /// Levenshtein similarity between 0 and 1.
    /// </summary>
    public static double Similarity(string first, string second)
    {
        if (first == second)
            return 1;

        if (first.Length == 0 || second.Length == 0)
            return 0;

        var distance = LevenshteinDistance(first, second);

        return 1d - ((double)distance / Math.Max(first.Length, second.Length));
    }

    public static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;

        // some providers return "yyyy-MM-dd HH:mm:ss"
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return date;

        return null;
    }

    private static int LevenshteinDistance(string first, string second)
    {
        var previous = new int[second.Length + 1];
        var current = new int[second.Length + 1];

        for (var j = 0; j <= second.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= first.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= second.Length; j++)
            {
                var cost = first[i - 1] == second[j - 1] ? 0 : 1;

                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[second.Length];
    }
}
