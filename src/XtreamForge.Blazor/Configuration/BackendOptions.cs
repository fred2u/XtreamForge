namespace XtreamForge.Blazor.Configuration;

public sealed class BackendOptions
{
    public const string SectionName = "Backend";

    public string BaseUrl { get; init; } = "http://xtreamforge";
}
