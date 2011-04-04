﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Core.Providers.Core;
using NzbDrone.Core.Repository;
using NzbDrone.Core.Repository.Quality;
using SubSonic.Repository;
using TvdbLib.Data;

namespace NzbDrone.Core.Providers
{
    public class SeriesProvider : ISeriesProvider
    {
        //TODO: Remove parsing of rest of tv show info we just need the show name

        //Trims all white spaces and separators from the end of the title.

        private readonly IConfigProvider _config;
        private readonly IRepository _sonioRepo;
        private readonly ITvDbProvider _tvDb;
        private readonly IQualityProvider _quality;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public SeriesProvider(IConfigProvider configProvider,
            IRepository dataRepository, ITvDbProvider tvDbProvider, IQualityProvider quality)
        {
            _config = configProvider;
            _sonioRepo = dataRepository;
            _tvDb = tvDbProvider;
            _quality = quality;
        }

        #region ISeriesProvider Members

        public IQueryable<Series> GetAllSeries()
        {
            return _sonioRepo.All<Series>();
        }

        public Series GetSeries(int seriesId)
        {
            return _sonioRepo.Single<Series>(s => s.SeriesId == seriesId);
        }

        /// <summary>
        /// Determines if a series is being actively watched.
        /// </summary>
        /// <param name="id">The TVDB ID of the series</param>
        /// <returns>Whether or not the show is monitored</returns>
        public bool IsMonitored(long id)
        {
            return _sonioRepo.Exists<Series>(c => c.SeriesId == id && c.Monitored);
        }

        public bool QualityWanted(int seriesId, QualityTypes quality)
        {
            var series = _sonioRepo.Single<Series>(seriesId);
            var profile = _quality.Find(series.QualityProfileId);

            return profile.Allowed.Contains(quality);
        }

        public TvdbSeries MapPathToSeries(string path)
        {
            var seriesPath = new DirectoryInfo(path);
            var searchResults = _tvDb.GetSeries(seriesPath.Name);

            if (searchResults == null)
                return null;

            return _tvDb.GetSeries(searchResults.Id, false);
        }

        public Series UpdateSeriesInfo(int seriesId)
        {
            var tvDbSeries = _tvDb.GetSeries(seriesId, true);
            var series = GetSeries(seriesId);

            series.SeriesId = tvDbSeries.Id;
            series.Title = tvDbSeries.SeriesName;
            series.AirTimes = tvDbSeries.AirsTime;
            series.AirsDayOfWeek = tvDbSeries.AirsDayOfWeek;
            series.Overview = tvDbSeries.Overview;
            series.Status = tvDbSeries.Status;
            series.Language = tvDbSeries.Language != null ? tvDbSeries.Language.Abbriviation : string.Empty;
            series.CleanTitle = Parser.NormalizeTitle(tvDbSeries.SeriesName);
            series.LastInfoSync = DateTime.Now;

            UpdateSeries(series);
            return series;
        }

        public void AddSeries(string path, int tvDbSeriesId, int qualityProfileId)
        {
            Logger.Info("Adding Series [{0}] Path: [{1}]", tvDbSeriesId, path);

            var repoSeries = new Series();
            repoSeries.SeriesId = tvDbSeriesId;
            repoSeries.Path = path;
            repoSeries.Monitored = true; //New shows should be monitored
            repoSeries.QualityProfileId = qualityProfileId;
            if (qualityProfileId == 0)
                repoSeries.QualityProfileId = Convert.ToInt32(_config.GetValue("DefaultQualityProfile", "1", true));

            repoSeries.SeasonFolder = _config.UseSeasonFolder;

            _sonioRepo.Add(repoSeries);
        }

        public Series FindSeries(string cleanTitle)
        {
            return _sonioRepo.Single<Series>(s => s.CleanTitle == cleanTitle);
        }

        public void UpdateSeries(Series series)
        {
            _sonioRepo.Update(series);
        }

        public void DeleteSeries(int seriesId)
        {
            Logger.Warn("Deleting Series [{0}]", seriesId);

            try
            {
                var series = _sonioRepo.Single<Series>(seriesId);

                //Delete Files, Episdes, Seasons then the Series
                //Can't use providers because episode provider needs series provider - Cyclic Dependency Injection, this will work

                Logger.Debug("Deleting EpisodeFiles from DB for Series: {0}", series.SeriesId);
                _sonioRepo.DeleteMany(series.EpisodeFiles);

                Logger.Debug("Deleting Episodes from DB for Series: {0}", series.SeriesId);
                _sonioRepo.DeleteMany(series.Episodes);

                Logger.Debug("Deleting Seasons from DB for Series: {0}", series.SeriesId);
                _sonioRepo.DeleteMany(series.Seasons);

                Logger.Debug("Deleting Series from DB {0}", series.Title);
                _sonioRepo.Delete<Series>(seriesId);

                Logger.Info("Successfully deleted Series [{0}]", seriesId);

            }
            catch (Exception e)
            {
                Logger.Error("An error has occurred while deleting series.", e);
                throw;
            }
        }

        public bool SeriesPathExists(string cleanPath)
        {
            if (_sonioRepo.Exists<Series>(s => s.Path == cleanPath))
                return true;

            return false;
        }

        #endregion

    }
}