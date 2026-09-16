using System.Text.Json.Nodes;

namespace XtreamForge.Xtream;

internal static class XtreamTmdbMetadata
{
    public static long? TryExtractTmdbId(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject)
            {
                if (property.Value is null)
                {
                    continue;
                }

                if (property.Key.Equals("tmdb_id", StringComparison.OrdinalIgnoreCase)
                    && TryParseTmdbId(property.Value) is long tmdbId)
                {
                    return tmdbId;
                }

                if (TryExtractTmdbId(property.Value) is long nestedTmdbId)
                {
                    return nestedTmdbId;
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var child in jsonArray)
            {
                if (TryExtractTmdbId(child) is long tmdbId)
                {
                    return tmdbId;
                }
            }
        }

        return null;
    }

    public static long? TryParseTmdbId(JsonNode? node) =>
        TryParseTmdbId(TryGetScalarString(node));

    public static long? TryParseTmdbId(string? value)
    {
        var normalizedValue = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedValue) || normalizedValue == "0")
        {
            return null;
        }

        return long.TryParse(normalizedValue, out var parsedValue) && parsedValue > 0
            ? parsedValue
            : null;
    }

    public static string? TryGetScalarString(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<string>(out var stringValue))
            {
                return stringValue;
            }

            if (jsonValue.TryGetValue<int>(out var intValue))
            {
                return intValue.ToString();
            }

            if (jsonValue.TryGetValue<long>(out var longValue))
            {
                return longValue.ToString();
            }
        }

        return null;
    }
}
