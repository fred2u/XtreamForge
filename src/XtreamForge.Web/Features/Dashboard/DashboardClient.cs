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

    /// <summary>Movie playbacks per day of the time zone of the Web server (the dates of the admin UI are shown in it) over the last 53 weeks.</summary>
    public async Task<WatchActivityDto> GetWatchActivityAsync(CancellationToken cancellationToken = default)
    {
        var timeZone = Uri.EscapeDataString(TimeZoneInfo.Local.Id);
        var payload = await httpClient.GetFromJsonAsync<WatchActivityDto>($"/api/admin/watch-history/activity?timeZone={timeZone}", cancellationToken);
        return payload ?? throw new InvalidOperationException("The watch activity response was empty.");
    }
}

/// <summary>Number of movie playbacks started on a day.</summary>
public sealed record WatchActivityDayDto(DateOnly Date, int Count);

/// <summary>Movie playbacks per day from <see cref="From"/> to <see cref="To"/> (included); the days without playback are omitted.</summary>
public sealed record WatchActivityDto(DateOnly From, DateOnly To, IReadOnlyList<WatchActivityDayDto> Days);

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
