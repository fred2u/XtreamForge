namespace XtreamForge.Data;

public sealed class XtreamSource
{
    public int Id { get; set; }

    public required string Protocol { get; set; }

    public required string Host { get; set; }

    public int Port { get; set; }

    public DateTimeOffset FirstSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<UpstreamCategory> UpstreamCategories { get; set; } = [];

    public ICollection<OutputCategory> OutputCategories { get; set; } = [];

    public ICollection<CategoryRule> CategoryRules { get; set; } = [];
}
