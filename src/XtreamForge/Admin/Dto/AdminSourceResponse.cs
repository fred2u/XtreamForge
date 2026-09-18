namespace XtreamForge.Admin.Dto;

public sealed record AdminSourceResponse(int Id, string Protocol, string Host, int Port, DateTimeOffset LastSeenAtUtc);
