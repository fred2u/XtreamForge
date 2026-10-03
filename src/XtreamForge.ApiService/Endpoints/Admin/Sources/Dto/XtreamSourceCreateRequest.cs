namespace XtreamForge.ApiService.Endpoints.Admin.Sources.Dto;

public sealed record XtreamSourceCreateRequest(
    string Url,
    string Username,
    string Password);
