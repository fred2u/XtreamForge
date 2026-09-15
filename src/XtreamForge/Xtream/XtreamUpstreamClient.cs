namespace XtreamForge.Xtream;

public sealed class XtreamUpstreamClient(HttpClient httpClient)
{
    public Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken = default) =>
        httpClient.SendAsync(request, completionOption, cancellationToken);
}
