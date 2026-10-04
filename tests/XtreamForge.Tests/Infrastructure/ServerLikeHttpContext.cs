using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace XtreamForge.Tests.Infrastructure;

/// <summary>
/// An <see cref="HttpContext"/> whose response reports <see cref="HttpResponse.HasStarted"/> once started and records
/// <see cref="HttpContext.Abort"/>, as a server does; the features of <see cref="DefaultHttpContext"/> never report a started response.
/// The response body is written to <see cref="Body"/>.
/// </summary>
public sealed class ServerLikeHttpContext
{
    private readonly ResponseFeature _response = new();
    private readonly LifetimeFeature _lifetime = new();

    public ServerLikeHttpContext()
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature());
        features.Set<IHttpResponseFeature>(_response);
        features.Set<IHttpResponseBodyFeature>(new ResponseBodyFeature(Body, _response));
        features.Set<IHttpRequestLifetimeFeature>(_lifetime);
        HttpContext = new DefaultHttpContext(features);
    }

    public DefaultHttpContext HttpContext { get; }

    public MemoryStream Body { get; } = new();

    public bool IsAborted => _lifetime.IsAborted;

    private sealed class ResponseFeature : HttpResponseFeature
    {
        public bool Started { get; set; }

        public override bool HasStarted => Started;
    }

    private sealed class ResponseBodyFeature(Stream body, ResponseFeature response) : StreamResponseBodyFeature(body)
    {
        public override Task StartAsync(CancellationToken cancellationToken = default)
        {
            response.Started = true;
            return base.StartAsync(cancellationToken);
        }
    }

    private sealed class LifetimeFeature : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; }

        public bool IsAborted { get; private set; }

        public void Abort() => IsAborted = true;
    }
}
