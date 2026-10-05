using XtreamForge.ApiService.Services.WatchHistory;
using XtreamForge.ApiService.Xtream;

namespace XtreamForge.ApiService.Endpoints.Xtream;

public sealed class XtreamRequestForwardEndpoint(IHttpClientFactory httpClientFactory, WatchHistoryQueue watchHistoryQueue, ILogger<XtreamRequestForwardEndpoint> logger)
{
    public async Task<IResult> ForwardAsync(XtreamContext xtreamContext, CancellationToken cancellationToken)
    {
        try
        {
            using var responseMessage = await XtreamHttpForwarder.SendAsync(httpClientFactory, xtreamContext, xtreamContext.BuildTargetUri(), cancellationToken);

            // the playback stays tracked while the stream is written, so that the other requests of the same playback are not recorded
            using var playback = TrackPlayback(xtreamContext, responseMessage);

            await XtreamHttpForwarder.WriteResponseAsync(responseMessage, xtreamContext.Response, cancellationToken);

            return Results.Empty;
        }
        catch (Exception exception) when (XtreamUpstreamFailure.IsUpstreamFailure(exception, cancellationToken))
        {
            return XtreamUpstreamFailure.Handle(exception, xtreamContext, logger);
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
