using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.Tmdb.Scoring;

/// <summary>
/// Seasons and episodes: maximum 20 points. Requires one TMDB call per season, hence evaluated in the advanced stage.
/// </summary>
public sealed class SeasonScoringRule(TmdbClient tmdbClient) : ITmdbScoringRule
{
    private const int MaximumScore = 20;
    private const int MaximumAirDateDeltaDays = 1;

    public TmdbScoringStage Stage => TmdbScoringStage.Advanced;

    public bool AppliesTo(ContentType type) => type == ContentType.Series;

    public async ValueTask<int> ScoreAsync(TmdbScoringContext context, CancellationToken cancellationToken)
    {
        var source = context.Source;

        // some providers have no "seasons" but do have "episodes"
        var seasonNumbers = source.Seasons.Select(season => season.SeasonNumber)
            .Concat(source.Episodes.Select(episode => episode.SeasonNumber))
            .Where(seasonNumber => seasonNumber > 0)
            .Distinct()
            .Order()
            .ToList();

        if (seasonNumbers.Count == 0)
            return 0;

        var comparison = new SeasonComparison();

        foreach (var seasonNumber in seasonNumbers)
        {
            var tmdbEpisodes = await tmdbClient.GetSeasonEpisodesAsync(context.Candidate.Id, seasonNumber, cancellationToken);

            // a season unknown by TMDB is suspicious but does not fail the whole matching
            if (tmdbEpisodes is not null)
                comparison.Compare(source, seasonNumber, tmdbEpisodes);
        }

        return Math.Min(comparison.ToScore(), MaximumScore);
    }

    private sealed class SeasonComparison
    {
        private int _comparableEpisodeCounts;
        private int _matchingEpisodeCounts;
        private int _comparableAirDates;
        private int _matchingAirDates;

        public void Compare(TmdbSourceItem source, int seasonNumber, IReadOnlyList<TmdbEpisode> tmdbEpisodes)
        {
            // episode counts are only compared when the provider explicitly declares them
            var sourceSeason = source.Seasons.FirstOrDefault(season => season.SeasonNumber == seasonNumber);
            if (sourceSeason is { EpisodeCount: > 0 })
            {
                _comparableEpisodeCounts++;
                if (tmdbEpisodes.Count == sourceSeason.EpisodeCount)
                    _matchingEpisodeCounts++;
            }

            foreach (var sourceEpisode in source.Episodes.Where(episode => episode.SeasonNumber == seasonNumber))
            {
                var tmdbAirDate = tmdbEpisodes.FirstOrDefault(episode => episode.EpisodeNumber == sourceEpisode.EpisodeNumber)?.AirDate;
                if (sourceEpisode.AirDate is not { } sourceAirDate || tmdbAirDate is null)
                    continue;

                _comparableAirDates++;
                if (Math.Abs((tmdbAirDate.Value.Date - sourceAirDate.Date).Days) <= MaximumAirDateDeltaDays)
                    _matchingAirDates++;
            }
        }

        public int ToScore()
        {
            // episode air dates are much more discriminating than episode counts
            if (_comparableAirDates > 0)
                return Ratio(_matchingAirDates, _comparableAirDates, 15) + Ratio(_matchingEpisodeCounts, _comparableEpisodeCounts, 5);

            // air dates unknown: 5 neutral points
            return 5 + Ratio(_matchingEpisodeCounts, _comparableEpisodeCounts, 10);
        }

        private static int Ratio(int matching, int comparable, int maximum)
            => comparable == 0 ? 0 : (int)Math.Round((double)matching / comparable * maximum);
    }
}
