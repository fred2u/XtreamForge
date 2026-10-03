namespace XtreamForge.Web.Features.Dashboard;

public sealed class DashboardClient(HttpClient httpClient)
{
    public async Task<DashboardStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync("/api/admin/status", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("The dashboard status request failed.");
        }

        var payload = await response.Content.ReadFromJsonAsync<DashboardStatusDto>(cancellationToken);
        return payload ?? throw new InvalidOperationException("The dashboard status response was empty.");
    }
}

public sealed record DashboardStatusDto(
    string ApplicationName,
    string ApplicationVersion,
    string Status,
    string DatabaseStatus,
    string DatabaseDetails,
    int SourceCount,
    int SourceCategoryCount,
    int RuleCount,
    int CustomCategoryCount,
    int ItemRuleCount,
    int TmdbRuleCount,
    int KnownTmdbMappingCount,
    int UnresolvedTmdbMappingCount,
    int TmdbInfoCount,
    int NotLoadedTmdbInfoCount,
    int ManuallyExcludedTmdbInfoCount);
