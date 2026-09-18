namespace XtreamForge.Admin.Dto;

public sealed record AdminSourceDiscoveryRequest(
    string? Protocol,
    string? HostOrBaseUrl,
    int? Port,
    string? Username,
    string? Password);
