using Microsoft.AspNetCore.Http.Extensions;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Xtream;

public sealed class XtreamContext(string protocol, string host, int port, string rest, HttpContext httpContext, RequestAction requestAction, ContentType contentType)
{
    public string Protocol => protocol;
    public string Host => host;
    public int Port => port;

    /// <summary>Upstream path without leading and trailing slashes, for example <c>player_api.php</c> or <c>movie/user/password/1.mp4</c>.</summary>
    public string Path => rest;

    public HttpRequest Request => httpContext.Request;
    public HttpResponse Response => httpContext.Response;

    public RequestAction Action => requestAction;
    public ContentType ContentType => contentType;

    public Uri BuildTargetUri()
    {
        return BuildTargetUri(Request.QueryString);
    }

    public Uri BuildTargetUri(KeyValuePair<string, string> queryParameter)
    {
        return BuildTargetUri(new Dictionary<string, string> { { queryParameter.Key, queryParameter.Value } });
    }

    public Uri BuildTargetUri(IReadOnlyDictionary<string, string> queryParameters)
    {
        var queryBuilder = new QueryBuilder(queryParameters);
        foreach (var entry in Request.Query.Where(entry => !queryParameters.ContainsKey(entry.Key)))
        {
            queryBuilder.Add(entry.Key, entry.Value.ToString());
        }
        return BuildTargetUri(queryBuilder.ToQueryString());
    }

    private Uri BuildTargetUri(QueryString queryString)
    {
        var encodedPath = string.IsNullOrEmpty(rest)
            ? "/"
            : $"/{string.Join('/', rest.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString))}";

        var uriBuilder = new UriBuilder(protocol, host, port)
        {
            Path = encodedPath,
            Query = queryString.Value is ['?', .. var query] ? query : string.Empty
        };

        return uriBuilder.Uri;
    }
}
