using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.TorrentRss
{
    public interface ITorrentRssSettingsDetector
    {
        TorrentRssIndexerParserSettings Detect(TorrentRssIndexerSettings settings);
    }

    public class TorrentRssSettingsDetector : ITorrentRssSettingsDetector
    {
        protected readonly Logger _logger;

        private readonly IHttpClient _httpClient;

        private const long ValidSizeThreshold = 2 * 1024 * 1024;

        public TorrentRssSettingsDetector(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Detect settings for Parser, based on URL
        /// </summary>
        /// <param name="settings">Indexer Settings to use for Parser</param>
        /// <returns>Parsed Settings or <c>null</c></returns>
        public TorrentRssIndexerParserSettings Detect(TorrentRssIndexerSettings indexerSettings)
        {
            _logger.Debug("Evaluating TorrentRss feed '{0}'", indexerSettings.BaseUrl);

            var requestGenerator = new TorrentRssIndexerRequestGenerator { Settings = indexerSettings };
            var request = requestGenerator.GetRecentRequests().First().First();

            HttpResponse httpResponse = null;
            try
            {
                httpResponse = _httpClient.Execute(request.HttpRequest);
            }
            catch (Exception ex)
            {
                _logger.WarnException(string.Format("Unable to connect to indexer {0}: {1}", request.Url, ex.Message), ex);
                return null;
            }

            try
            {
                var indexerResponse = new IndexerResponse(request, httpResponse);
                return GetParserSettings(indexerResponse, indexerSettings);
            }
            catch (Exception ex)
            {
                _logger.WarnException(string.Format("An error occurred while parsing the feed at '{0}': {1}", request.Url, ex.Message), ex);
                return null;
            }
        }

        private TorrentRssIndexerParserSettings GetParserSettings(IndexerResponse response, TorrentRssIndexerSettings indexerSettings)
        {
            var settings = GetEzrssParserSettings(response, indexerSettings);
            if (settings != null)
            {
                return settings;
            }

            settings = GetGenericTorrentRssParserSettings(response, indexerSettings);
            if (settings != null)
            {
                return settings;
            }

            return null;
        }

        private TorrentRssIndexerParserSettings GetEzrssParserSettings(IndexerResponse response, TorrentRssIndexerSettings indexerSettings)
        {
            if (!IsEZTVFeed(response))
            {
                return null;
            }

            _logger.Trace("Feed has Ezrss schema");

            var parser = new EzrssTorrentRssParser();
            var releases = ParseResponse(parser, response);

            if (ValidateReleases(releases, indexerSettings) &&
                ValidateReleaseSize(releases, indexerSettings))
            {
                _logger.Debug("Feed was parseable by Ezrss Parser");
                return new TorrentRssIndexerParserSettings
                {
                    UseEZTVFormat = true
                };
            }

            _logger.Trace("Feed wasn't parsable by Ezrss Parser");
            return null;
        }

        private TorrentRssIndexerParserSettings GetGenericTorrentRssParserSettings(IndexerResponse response, TorrentRssIndexerSettings indexerSettings)
        {
            var settings = new TorrentRssIndexerParserSettings
            {
                UseEnclosureLength = true,
                ParseSeedersInDescription = true
            };

            var parser = new TorrentRssParser
            {
                UseEnclosureLength = true,
                ParseSeedersInDescription = true
            };

            var releases = ParseResponse(parser, response);
            if (!ValidateReleases(releases, indexerSettings))
            {
                return null;
            }

            if (!releases.Any(v => v.Seeders.HasValue))
            {
                _logger.Trace("Feed doesn't have Seeders in Description, disabling option.");
                parser.ParseSeedersInDescription = settings.ParseSeedersInDescription = false;
            }
            
            if (!releases.Any(r => r.Size < ValidSizeThreshold))
            {
                _logger.Trace("Feed has valid size in enclosure.");
                return settings;
            }

            parser.UseEnclosureLength = settings.UseEnclosureLength = false;
            parser.ParseSizeInDescription = settings.ParseSizeInDescription = true;

            releases = ParseResponse(parser, response);
            if (!ValidateReleases(releases, indexerSettings))
            {
                return null;
            }
                
            if (!releases.Any(r => r.Size < ValidSizeThreshold))
            {
                _logger.Trace("Feed has valid size in description.");
                return settings;
            }
           
            parser.ParseSizeInDescription = settings.ParseSizeInDescription = false;
            parser.SizeElementName = settings.SizeElementName = "Size";

            releases = ParseResponse(parser, response);
            if (!ValidateReleases(releases, indexerSettings))
            {
                return null;
            }

            if (!releases.Any(r => r.Size < ValidSizeThreshold))
            {
                _logger.Trace("Feed has valid size in Size element.");
                return settings;
            }

            _logger.Debug("Feed doesn't have release size.");

            parser.SizeElementName = settings.SizeElementName = null;

            releases = ParseResponse(parser, response);

            if (!ValidateReleaseSize(releases, indexerSettings))
            {
                return null;
            }

            return settings;
        }

        private Boolean IsEZTVFeed(IndexerResponse response)
        {
            using (var xmlTextReader = XmlReader.Create(new StringReader(response.Content), new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, ValidationType = ValidationType.None, IgnoreComments = true, XmlResolver = null }))
            {
                var document = XDocument.Load(xmlTextReader);

                // Check Namespace
                if (document.Root == null)
                {
                    throw new InvalidDataException("Could not parse IndexerResponse into XML.");
                }

                var ns = document.Root.GetNamespaceOfPrefix("torrent");
                if (ns == "http://xmlns.ezrss.it/0.1/")
                {
                    _logger.Trace("Identified feed as EZTV compatible by EZTV Namespace");
                    return true;
                }

                // Check DTD in DocType
                if (document.DocumentType != null && document.DocumentType.SystemId == "http://xmlns.ezrss.it/0.1/dtd/")
                {
                    _logger.Trace("Identified feed as EZTV compatible by EZTV DTD");
                    return true;
                }

                return false;
            }
        }

        private TorrentInfo[] ParseResponse(IParseIndexerResponse parser, IndexerResponse response)
        {
            try
            {
                var releases = parser.ParseResponse(response).Cast<TorrentInfo>().ToArray();
                return releases;

            }
            catch (Exception ex)
            {
                _logger.DebugException("Unable to parse indexer feed: " + ex.Message, ex);
                return null;
            }
        }

        private bool ValidateReleases(TorrentInfo[] releases, TorrentRssIndexerSettings indexerSettings)
        {
            if (releases == null)
            {
                return false;
            }
             
            if (releases.Empty())
            {
                _logger.Trace("Empty feed, cannot check if feed is parsable.");
                return false;
            }

            var torrentInfo = releases.First();

            _logger.Trace("TorrentInfo: \n{0}", torrentInfo.ToString("L"));

            if (releases.Any(r => r.Title.IsNullOrWhiteSpace()))
            {
                _logger.Trace("Feed contains releases without title.");
                return false;
            }

            if (releases.Any(r => !IsValidDownloadUrl(r.DownloadUrl)))
            {
                _logger.Trace("Failed to find a valid download url in the feed.");
                return false;
            }

            var total = releases.Where(v => v.Guid != null).Select(v => v.Guid).ToArray();
            var distinct = total.Distinct().ToArray();

            if (distinct.Length != total.Length)
            {
                _logger.Trace("Feed contains releases with same guid, rejecting malformed rss feed.");
                return false;
            }

            return true;
        }

        private bool ValidateReleaseSize(TorrentInfo[] releases, TorrentRssIndexerSettings indexerSettings)
        {
            if (!indexerSettings.AllowZeroSize && releases.Any(r => r.Size == 0))
            {
                _logger.Trace("Feed doesn't contain the content size.");
                return false;
            }

            if (releases.Any(r => r.Size != 0 && r.Size < ValidSizeThreshold))
            {
                _logger.Trace("Size of one more releases lower than {0}, feed must contain content size.", ValidSizeThreshold.SizeSuffix());
                return false;
            }

            return true;
        }

        private static bool IsValidDownloadUrl(string url)
        {
            if (url.IsNullOrWhiteSpace())
            {
                return false;
            }

            if (url.StartsWith("magnet:"))
            {
                return true;
            }

            if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                return true;
            }

            return false;
        }
    }
}
