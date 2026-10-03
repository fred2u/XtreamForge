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
        var extensionIndex = fileName.IndexOf('.', StringComparison.Ordinal);
        var streamId = extensionIndex < 0 ? fileName : fileName[..extensionIndex];

        if (username.Length == 0 || password.Length == 0 || streamId.Length is 0 or > MaximumStreamIdLength || !streamId.All(char.IsAsciiDigit))
            return null;

        return new XtreamMovieStream(username, password, streamId);
    }
}
