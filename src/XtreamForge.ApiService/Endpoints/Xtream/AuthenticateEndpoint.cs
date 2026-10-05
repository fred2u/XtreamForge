using System.Globalization;
using System.Text.Json.Nodes;
using XtreamForge.ApiService.Xtream;

namespace XtreamForge.ApiService.Endpoints.Xtream;

/// <summary>
/// Handles the authentication (<c>player_api.php</c> without action): the upstream <c>server_info</c> is rewritten to point to XtreamForge,
/// so that clients building stream URLs from it play through XtreamForge, and the upstream of a successfully authenticated account
/// is remembered in <see cref="XtreamAccountDirectory"/> to forward these stream requests.
/// </summary>
public class AuthenticateEndpoint(IHttpClientFactory httpClientFactory, XtreamAccountDirectory accountDirectory, ILogger<AuthenticateEndpoint> logger)
{
    public async Task<IResult> GetAsync(XtreamContext xtreamContext, CancellationToken cancellationToken)
    {
        if (xtreamContext.Action != RequestAction.Authenticate)
        {
            return Results.BadRequest("Invalid request action.");
        }

        try
        {
            using var responseMessage = await XtreamHttpForwarder.SendAsync(httpClientFactory, xtreamContext, xtreamContext.BuildTargetUri(), cancellationToken);

            if (!responseMessage.IsSuccessStatusCode)
            {
                await XtreamHttpForwarder.WriteResponseAsync(responseMessage, xtreamContext.Response, cancellationToken);
                return Results.Empty;
            }

            // the authentication payload is small and must be rewritten, so it is buffered
            await using var payloadStream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonNode.ParseAsync(payloadStream, cancellationToken: cancellationToken);

            if (payload is JsonObject authentication && IsAuthenticated(authentication))
            {
                RememberAccount(xtreamContext);
                RewriteServerInfo(authentication, xtreamContext.Request);
            }

            await xtreamContext.Response.WriteAsJsonAsync(payload, cancellationToken);

            return Results.Empty;
        }
        catch (Exception exception) when (XtreamUpstreamFailure.IsUpstreamFailure(exception, cancellationToken))
        {
            return XtreamUpstreamFailure.Handle(exception, xtreamContext, logger);
        }
    }

    // Xtream panels return "auth": 1 (number or string) for valid credentials, 0 otherwise
    private static bool IsAuthenticated(JsonObject authentication)
        => authentication["user_info"] is JsonObject userInfo && userInfo["auth"]?.ToString() == "1";

    private void RememberAccount(XtreamContext xtreamContext)
    {
        var username = xtreamContext.Request.Query["username"].ToString();
        var password = xtreamContext.Request.Query["password"].ToString();

        if (username.Length > 0 && password.Length > 0)
            accountDirectory.Remember(username, password, new XtreamUpstream(xtreamContext.Protocol, xtreamContext.Host, xtreamContext.Port));
    }

    // clients build the stream URLs as {server_protocol}://{url}:{port}/movie/{username}/{password}/{id}.{extension}
    private static void RewriteServerInfo(JsonObject authentication, HttpRequest request)
    {
        if (authentication["server_info"] is not JsonObject serverInfo)
            return;

        var port = (request.Host.Port ?? (request.IsHttps ? 443 : 80)).ToString(CultureInfo.InvariantCulture);

        serverInfo["url"] = request.Host.Host;
        serverInfo["port"] = port;
        serverInfo["https_port"] = port;
        serverInfo["server_protocol"] = request.Scheme;
    }
}
