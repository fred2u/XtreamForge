namespace XtreamForge.ApiService.Options;

public sealed class XtreamProxyOptions
{
    public const string SectionName = "XtreamProxy";
    public const string HttpClientName = "XtreamHttpClient";

    public bool AllowAnyDestination { get; init; }

    public string[] AllowedHosts { get; init; } = [];
}
