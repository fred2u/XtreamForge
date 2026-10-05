using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.TmdbInfos;

/// <summary>
/// Background request to load the TMDB metadata of a movie or TV show; carries no upstream credentials.
/// </summary>
public sealed record TmdbInfoRequest(ContentType Type, long TmdbId);
