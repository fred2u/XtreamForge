using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.TmdbIdRetriever;

/// <summary>
/// Background TMDB ID lookup request. Carries the upstream credentials, which are kept in memory only.
/// </summary>
public sealed record TmdbIdRetrieverRequest(int XtreamSourceId, string Protocol, string Host, int Port, string Username, string Password, string StreamId, ContentType Type, string? StreamIcon = null)
{
    // never expose the credentials through the compiler-generated ToString
    public override string ToString() => $"{nameof(TmdbIdRetrieverRequest)} {{ XtreamSourceId = {XtreamSourceId}, StreamId = {StreamId}, Type = {Type} }}";
}
