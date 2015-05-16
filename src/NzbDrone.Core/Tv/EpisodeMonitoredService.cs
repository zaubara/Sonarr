﻿using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Tv
{
    public interface IEpisodeMonitoredService
    {
        void SetEpisodeMonitoredStatus(Series series, MonitoringOptions monitoringOptions);
    }

    public class EpisodeMonitoredService : IEpisodeMonitoredService
    {
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly Logger _logger;

        public EpisodeMonitoredService(ISeriesService seriesService, IEpisodeService episodeService, Logger logger)
        {
            _seriesService = seriesService;
            _episodeService = episodeService;
            _logger = logger;
        }

        public void SetEpisodeMonitoredStatus(Series series, MonitoringOptions monitoringOptions)
        {
            _logger.Debug("[{0}] Setting episode monitored status.", series.Title);
            
            var episodes = _episodeService.GetEpisodeBySeries(series.Id);

            if (monitoringOptions.IgnoreEpisodesWithFiles)
            {
                _logger.Debug("Ignoring Episodes with Files");
                ToggleEpisodesMonitoredState(episodes.Where(e => e.HasFile), false);
            }

            else
            {
                _logger.Debug("Monitoring Episodes with Files");
                ToggleEpisodesMonitoredState(episodes.Where(e => e.HasFile), true);
            }

            if (monitoringOptions.IgnoreEpisodesWithoutFiles)
            {
                _logger.Debug("Ignoring Episodes without Files");
                ToggleEpisodesMonitoredState(episodes.Where(e => !e.HasFile && e.AirDateUtc.HasValue && e.AirDateUtc.Value.Before(DateTime.UtcNow)), false);
            }

            else
            {
                _logger.Debug("Monitoring Episodes without Files");
                ToggleEpisodesMonitoredState(episodes.Where(e => !e.HasFile && e.AirDateUtc.HasValue && e.AirDateUtc.Value.Before(DateTime.UtcNow)), true);
            }

            var lastSeason = series.Seasons.Select(s => s.SeasonNumber).MaxOrDefault();

            foreach (var s in series.Seasons)
            {
                var season = s;

                if (season.Monitored)
                {
                    if (!monitoringOptions.IgnoreEpisodesWithFiles && !monitoringOptions.IgnoreEpisodesWithoutFiles)
                    {
                        ToggleEpisodesMonitoredState(episodes.Where(e => e.SeasonNumber == season.SeasonNumber), true);
                    }
                }

                else
                {
                    ToggleEpisodesMonitoredState(episodes.Where(e => e.SeasonNumber == season.SeasonNumber), false);
                }

                if (season.SeasonNumber < lastSeason)
                {
                    if (episodes.Where(e => e.SeasonNumber == season.SeasonNumber).All(e => !e.Monitored))
                    {
                        season.Monitored = false;
                    }
                }
            }

            _seriesService.UpdateSeries(series);
            _episodeService.UpdateEpisodes(episodes);
        }

        private void ToggleEpisodesMonitoredState(IEnumerable<Episode> episodes, bool monitored)
        {
            foreach (var episode in episodes)
            {
                episode.Monitored = monitored;
            }
        }
    }
}
