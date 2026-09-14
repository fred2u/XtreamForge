namespace XtreamForge.Configuration;

public sealed class XtreamOptions
{
    public const string SectionName = "Xtream";

    public string BaseUrl { get; init; } = string.Empty;

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}
