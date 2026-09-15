using System.Net.Http.Json;

namespace XtreamForge.Blazor.Features.Dashboard;

public sealed class DashboardClient(HttpClient httpClient)
{
    public async Task<DashboardStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync("/api/admin/status", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("The dashboard status request failed.");
        }

        var payload = await response.Content.ReadFromJsonAsync<DashboardStatus>(cancellationToken);
        return payload ?? throw new InvalidOperationException("The dashboard status response was empty.");
    }
}

public sealed record DashboardStatus(
    string ApplicationName,
    string ApplicationVersion,
    string Status,
    string DatabaseStatus,
    string DatabaseDetails);
