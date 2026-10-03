namespace XtreamForge.Web.Configuration;

public sealed class BackendOptions
{
    public const string SectionName = "Backend";

    public string BaseUrl { get; init; } = "https://xtreamforge-apiservice";
}
