namespace XtreamForge.Api.Configuration;

public sealed class XtreamProxyOptions
{
    public const string SectionName = "XtreamProxy";

    public bool AllowAnyDestination { get; init; }

    public string[] AllowedHosts { get; init; } = [];
}
