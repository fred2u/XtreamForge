namespace XtreamForge.ApiService.Endpoints.Admin.Sources.Dto;

public sealed record XtreamSourceDto(
    int Id,
    string Protocol,
    string Host,
    int Port);
