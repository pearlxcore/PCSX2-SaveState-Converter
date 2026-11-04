using System.Text.RegularExpressions;

namespace SaveStateConverter;

public static partial class TextSearch
{
    /// <summary>
    /// True when every word of the query appears in one of the fields, ignoring case. Short numbers
    /// must match a whole number ("1" finds slot 1 but not SLUS-20013), longer ones can match the
    /// start of one. An empty query matches everything.
    /// </summary>
    public static bool Matches(string? query, params string?[] fields)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(word => fields.Any(field => field is not null && Contains(field, word)));
    }

    private static bool Contains(string field, string word)
    {
        if (!word.All(char.IsAsciiDigit))
            return field.Contains(word, StringComparison.OrdinalIgnoreCase);
        foreach (Match number in Numbers().Matches(field))
        {
            if (word.Length <= 2 ? number.Value == word : number.Value.StartsWith(word, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Numbers();
}
