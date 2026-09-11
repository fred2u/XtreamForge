namespace XtreamForge.Infrastructure.Data;

public sealed class Setting
{
    public int Id { get; set; }

    public required string Key { get; set; }

    public string? Value { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
