using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Endpoints.Admin.Dashboard.Dto;
using XtreamForge.Database;

namespace XtreamForge.ApiService.Endpoints.Admin.Dashboard;

public class DashboardStatusGetEndpoint(XtreamForgeDbContext dbContext, ILogger<DashboardStatusGetEndpoint> logger)
{
    public async Task<IResult> GetAsync(CancellationToken cancellationToken = default)
    {
        string databaseStatus;
        string databaseDetails;
        var sourceCount = 0;
        var sourceCategoryCount = 0;
        var ruleCount = 0;
        var customCategoryCount = 0;
        var itemRuleCount = 0;
        var tmdbRuleCount = 0;
        TmdbMappingCounts? tmdbMappingCounts = null;
        TmdbInfoCounts? tmdbInfoCounts = null;

        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            var providerDisplayName = "PostgreSQL";

            databaseStatus = canConnect ? "Connected" : "Unavailable";
            databaseDetails = canConnect
                ? $"{providerDisplayName} is reachable."
                : $"{providerDisplayName} is not reachable.";

            if (canConnect)
            {
                sourceCount = await dbContext.XtreamSources.CountAsync(cancellationToken);
                sourceCategoryCount = await dbContext.XtreamCategories.CountAsync(cancellationToken);
                ruleCount = await dbContext.CategoryRules.CountAsync(cancellationToken);
                customCategoryCount = await dbContext.CustomCategories.CountAsync(cancellationToken);
                itemRuleCount = await dbContext.ItemRules.CountAsync(cancellationToken);
                tmdbRuleCount = await dbContext.TmdbRules.CountAsync(cancellationToken);

                // one query per table; no row (empty table) leaves the counts at zero
                tmdbMappingCounts = await dbContext.StreamTmdbMappings
                    .GroupBy(_ => 1)
                    .Select(mappings => new TmdbMappingCounts(
                        mappings.Count(mapping => mapping.TmdbId != null),
                        mappings.Count(mapping => mapping.TmdbId == null)))
                    .FirstOrDefaultAsync(cancellationToken);
                tmdbInfoCounts = await dbContext.TmdbInfos
                    .GroupBy(_ => 1)
                    .Select(infos => new TmdbInfoCounts(
                        infos.Count(),
                        infos.Count(info => info.LoadedAtUtc == null),
                        infos.Count(info => info.IsExcluded)))
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            // the dashboard stays available and reports the database as unavailable
            logger.LogWarning(exception, "Dashboard status could not be read from the database");
            databaseStatus = "Unavailable";
            databaseDetails = "The database status could not be read.";
        }

        return TypedResults.Ok(new DashboardStatusDto(
            ApplicationName: "XtreamForge",
            ApplicationVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
            Status: "Healthy",
            DatabaseStatus: databaseStatus,
            DatabaseDetails: databaseDetails,
            SourceCount: sourceCount,
            SourceCategoryCount: sourceCategoryCount,
            RuleCount: ruleCount,
            CustomCategoryCount: customCategoryCount,
            ItemRuleCount: itemRuleCount,
            TmdbRuleCount: tmdbRuleCount,
            KnownTmdbMappingCount: tmdbMappingCounts?.Known ?? 0,
            UnresolvedTmdbMappingCount: tmdbMappingCounts?.Unresolved ?? 0,
            TmdbInfoCount: tmdbInfoCounts?.Total ?? 0,
            NotLoadedTmdbInfoCount: tmdbInfoCounts?.NotLoaded ?? 0,
            ManuallyExcludedTmdbInfoCount: tmdbInfoCounts?.ManuallyExcluded ?? 0));
    }

    private sealed record TmdbMappingCounts(int Known, int Unresolved);

    private sealed record TmdbInfoCounts(int Total, int NotLoaded, int ManuallyExcluded);
}
