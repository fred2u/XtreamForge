using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Xtream;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.ApiService.Endpoints.Xtream;

public sealed class XtreamRequestForwardEndpoint(IHttpClientFactory httpClientFactory, WatchHistoryQueue watchHistoryQueue, ILogger<XtreamRequestForwardEndpoint> logger)
{
    public async Task<IResult> ForwardAsync(XtreamContext xtreamContext, CancellationToken cancellationToken)
    {
        try
        {
            using var requestMessage = XtreamHttpRequestMessageFactory.Create(xtreamContext.BuildTargetUri(), xtreamContext.Request);

            var httpClient = httpClientFactory.CreateClient(XtreamProxyOptions.HttpClientName);
            using var responseMessage = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            // the playback stays tracked while the stream is written, so that the other requests of the same playback are not recorded
            using var playback = TrackPlayback(xtreamContext, responseMessage);

            await XtreamHttpResponseMessageWriter.WriteResponseAsync(responseMessage, xtreamContext.Response, xtreamContext.Request.Method, cancellationToken);

            return Results.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(exception, "ErrorMessage: {ErrorMessage}", XtreamCredentialRedaction.SanitizeText(exception.Message));

            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "ErrorMessage: {ErrorMessage}", XtreamCredentialRedaction.SanitizeText(exception.Message));

            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    // only a movie stream served or redirected by the provider is a playback; HEAD requests only probe the stream.
    // Redirects are not followed (AllowAutoRedirect is disabled): Xtream panels often redirect a stream to a load balancer,
    // which the client then calls directly
    private IDisposable? TrackPlayback(XtreamContext xtreamContext, HttpResponseMessage responseMessage)
    {
        if ((int)responseMessage.StatusCode is < 200 or >= 400
            || !HttpMethods.IsGet(xtreamContext.Request.Method)
            || XtreamStreamPath.ParseMovie(xtreamContext.Path) is not { } movie)
            return null;

        return watchHistoryQueue.TrackPlayback(xtreamContext.Protocol, xtreamContext.Host, xtreamContext.Port, movie);
    }
}
