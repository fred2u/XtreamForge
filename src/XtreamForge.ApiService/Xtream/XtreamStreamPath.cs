namespace XtreamForge.ApiService.Xtream;

/// <summary>
/// Movie stream requested by a client. Carries the upstream credentials of the path, which are kept in memory only.
/// </summary>
public sealed record XtreamMovieStream(string Username, string Password, string StreamId)
{
    // never expose the credentials through the compiler-generated ToString
    public override string ToString() => $"{nameof(XtreamMovieStream)} {{ StreamId = {StreamId} }}";
}

public static class XtreamStreamPath
{
    private const string MovieSegment = "movie";
    private const int MaximumStreamIdLength = 64;

    // stream kinds of the {kind}/{username}/{password}/... paths
    private static readonly string[] StreamKinds = ["movie", "series", "live", "timeshift"];

    /// <summary>
    /// Whether <paramref name="path"/> is a media stream: <c>{kind}/{username}/{password}/...</c> for the <c>movie</c>, <c>series</c>,
    /// <c>live</c>, and <c>timeshift</c> kinds, or the short live form <c>{username}/{password}/{streamId}</c> (the extension is optional).
    /// </summary>
    public static bool IsStream(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.Split('/') switch
        {
            [var kind, { Length: > 0 }, { Length: > 0 }, _, ..] => StreamKinds.Contains(kind, StringComparer.OrdinalIgnoreCase),
            [{ Length: > 0 }, { Length: > 0 }, var fileName] => ReadStreamId(fileName) is not null,
            _ => false
        };
    }

    /// <summary>
    /// Reads a movie stream path, <c>movie/{username}/{password}/{streamId}.{extension}</c> (the extension is optional);
    /// returns null for any other path. Xtream stream IDs are numbers, so any other value is rejected.
    /// </summary>
    public static XtreamMovieStream? ParseMovie(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var segments = path.Split('/');
        if (segments.Length != 4 || !segments[0].Equals(MovieSegment, StringComparison.OrdinalIgnoreCase))
            return null;

        var (username, password, fileName) = (segments[1], segments[2], segments[3]);
        if (username.Length == 0 || password.Length == 0 || ReadStreamId(fileName) is not { } streamId)
            return null;

        return new XtreamMovieStream(username, password, streamId);
    }

    // {streamId}.{extension}, the extension being optional; null when the stream ID is not a number
    private static string? ReadStreamId(string fileName)
    {
        var extensionIndex = fileName.IndexOf('.', StringComparison.Ordinal);
        var streamId = extensionIndex < 0 ? fileName : fileName[..extensionIndex];

        return streamId.Length is 0 or > MaximumStreamIdLength || !streamId.All(char.IsAsciiDigit) ? null : streamId;
    }
}
