namespace XtreamForge.Endpoints;

public static class StatusEndpoints
{
    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var apiGroup = endpoints.MapGroup("/api");

        apiGroup.MapGet("/status", () =>
            TypedResults.Ok(new ApiStatusResponse(
                ApplicationName: "XtreamForge",
                ApplicationVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
                Status: "Healthy")))
            .WithName("GetStatus")
            .WithSummary("Gets the current XtreamForge application status.");

        return endpoints;
    }

    private sealed record ApiStatusResponse(string ApplicationName, string ApplicationVersion, string Status);
}
