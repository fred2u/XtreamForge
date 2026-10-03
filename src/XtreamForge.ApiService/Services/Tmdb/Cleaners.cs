using System.Text.RegularExpressions;

namespace XtreamForge.ApiService.Services.Tmdb;

public static partial class Cleaners
{
    [GeneratedRegex(@"(?:\|\s*[A-Za-z]{2,5}\s*\||\[\s*[A-Za-z]{2,5}\s*\])", RegexOptions.IgnoreCase, "en-BE")]
    private static partial Regex PrefixTags();

    // uppercase language tag followed by a separator at the start of the title, e.g. "FR ★ " or "EN - "
    [GeneratedRegex(@"^\s*[A-Z]{2,3}\s*[★☆•●|\-–]\s*")]
    private static partial Regex LeadingLanguageTag();

    [GeneratedRegex(@"\b(4k|uhd|fhd|hd|sd|hdr|hevc|dv|dolby|vision|multi|mult|multilingue|webrip|web-dl|bluray|x264|x265|h.265)\b", RegexOptions.IgnoreCase, "en-BE")]
    private static partial Regex TechnicalTags();

    // audio or subtitle language markers, e.g. "VOSTFR" or "[TRUEFRENCH]"
    [GeneratedRegex(@"\b(vf|vostfr|vff|truefrench|vfq)\b", RegexOptions.IgnoreCase, "en-BE")]
    private static partial Regex LanguageTags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpace();

    [GeneratedRegex(@"(?:\(\s*|\|\s*)*(?:19|20)\d{2}\s*(?:\)|\|)*")]
    private static partial Regex Year();

    // a trailing year must be delimited ("(2021)", "| 2021", " - 2021") so that titles such as "Blade Runner 2049" are kept
    [GeneratedRegex(@"(?:\(\s*|\|\s*|\s[-–]\s*)(?:19|20)\d{2}\s*(?:\)|\|)*")]
    private static partial Regex YearSafe();

    [GeneratedRegex(@"(?:19|20)\d{2}$")]
    private static partial Regex EndsWithYear();

    [GeneratedRegex(@"(\(\s*\)|\[\s*\])")]
    private static partial Regex EmptyBrackets();

    public static string CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        var value = PrefixTags().Replace(title, string.Empty).Trim();
        value = LeadingLanguageTag().Replace(value, string.Empty);

        // remove the last year explicitly delimited
        if (value.Length > 7)
        {
            var matches = EndsWithYear().IsMatch(value)
                ? YearSafe().Matches(value)
                : Year().Matches(value);

            if (matches.Count > 0)
            {
                var match = matches[^1];
                value = value.Remove(match.Index, match.Length);
            }
        }

        value = TechnicalTags().Replace(value, string.Empty);
        value = LanguageTags().Replace(value, string.Empty);

        value = EmptyBrackets().Replace(value, string.Empty);
        value = MultiSpace().Replace(value, " ");

        return value.Trim(' ', '-', '.', '_', '|');
    }
}
