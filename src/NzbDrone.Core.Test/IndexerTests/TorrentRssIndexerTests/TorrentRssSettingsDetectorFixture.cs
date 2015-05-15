﻿using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers.TorrentRss;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.TorrentRssIndexerTests
{
    [TestFixture]
    public class TorrentRssSettingsDetectorFixture : CoreTest<TorrentRssSettingsDetector>
    {
        private const string _indexerUrl = "http://my.indexer.tv/recent";
        private TorrentRssIndexerSettings _indexerSettings;

        [SetUp]
        public void SetUp()
        {
            _indexerSettings = new TorrentRssIndexerSettings { BaseUrl = _indexerUrl };
        }

        private void GivenRecentFeedResponse(string rssXmlFile)
        {
            var recentFeed = ReadAllText(@"Files/Indexers/" + rssXmlFile);

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.Execute(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), recentFeed));
        }

        [Test]
        public void should_detect_rss_settings_for_eztv()
        {
            GivenRecentFeedResponse("Eztv/Eztv.xml");

            var settings = Subject.Detect(_indexerSettings);

            settings.ShouldBeEquivalentTo(new TorrentRssIndexerParserSettings
                {
                    UseEZTVFormat = true,
                    UseEnclosureLength = false,
                    ParseSizeInDescription = false,
                    ParseSeedersInDescription = false,
                    SizeElementName = null
                });
        }

        [Test]
        public void should_detect_rss_settings_for_speed_cd()
        {
            GivenRecentFeedResponse("TorrentRss/speed.cd.xml");

            var settings = Subject.Detect(_indexerSettings);

            settings.ShouldBeEquivalentTo(new TorrentRssIndexerParserSettings
            {
                UseEZTVFormat = false,
                UseEnclosureLength = false,
                ParseSizeInDescription = true,
                ParseSeedersInDescription = false,
                SizeElementName = null
            });
        }

        [Test]
        public void should_detect_rss_settings_for_ImmortalSeed()
        {
            GivenRecentFeedResponse("TorrentRss/ImmortalSeed.xml");

            var settings = Subject.Detect(_indexerSettings);

            settings.ShouldBeEquivalentTo(new TorrentRssIndexerParserSettings
            {
                UseEZTVFormat = false,
                UseEnclosureLength = false,
                ParseSizeInDescription = true,
                ParseSeedersInDescription = true,
                SizeElementName = null
            });
        }

        [Test]
        public void should_detect_rss_settings_for_ShowRSS_info()
        {
            _indexerSettings.AllowZeroSize = true;

            GivenRecentFeedResponse("TorrentRss/ShowRSS.info.xml");

            var settings = Subject.Detect(_indexerSettings);

            settings.ShouldBeEquivalentTo(new TorrentRssIndexerParserSettings
            {
                UseEZTVFormat = false,
                UseEnclosureLength = false,
                ParseSizeInDescription = false,
                ParseSeedersInDescription = false,
                SizeElementName = null
            });
        }

        [TestCase("BitMeTv/BitMeTv.xml")]
        [TestCase("Fanzub/fanzub.xml")]
        [TestCase("KickassTorrents/KickassTorrents.xml")]
        [TestCase("IPTorrents/IPTorrents.xml")]
        [TestCase("Newznab/newznab_nzb_su.xml")]
        [TestCase("Nyaa/Nyaa.xml")]
        [TestCase("Omgwtfnzbs/Omgwtfnzbs.xml")]
        [TestCase("Torznab/torznab_hdaccess_net.xml")]
        [TestCase("Torznab/torznab_tpb.xml")]
        public void should_detect_recent_feed(string rssXmlFile)
        {
            GivenRecentFeedResponse(rssXmlFile);

            var settings = Subject.Detect(_indexerSettings);

            settings.Should().NotBeNull();
        }

        [TestCase("TorrentRss/invalid/Eztv_InvalidDownloadUrl.xml")]
        [TestCase("TorrentRss/invalid/ImmortalSeed_InvalidDownloadUrl.xml")]
        public void should_reject_recent_feed(string rssXmlFile)
        {
            GivenRecentFeedResponse(rssXmlFile);

            var settings = Subject.Detect(_indexerSettings);

            settings.Should().BeNull();
        }

        [TestCase("Torrentleech/Torrentleech.xml")]
        [TestCase("Wombles/wombles.xml")]
        [TestCase("TorrentRss/invalid/Eztv_InvalidSize.xml")]
        [TestCase("TorrentRss/invalid/ImmortalSeed_InvalidSize.xml")]
        public void should_detect_feed_without_size(string rssXmlFile)
        {
            _indexerSettings.AllowZeroSize = true;

            GivenRecentFeedResponse(rssXmlFile);

            var settings = Subject.Detect(_indexerSettings);

            settings.Should().NotBeNull();

            settings.UseEnclosureLength.Should().BeFalse();
            settings.ParseSizeInDescription.Should().BeFalse();
            settings.SizeElementName.Should().BeNull();
        }

        [TestCase("TorrentRss/invalid/Eztv_InvalidSize.xml")]
        [TestCase("TorrentRss/invalid/ImmortalSeed_InvalidSize.xml")]
        public void should_reject_feed_without_size(string rssXmlFile)
        {
            GivenRecentFeedResponse(rssXmlFile);

            var settings = Subject.Detect(_indexerSettings);

            settings.Should().BeNull();
        }
    }
}
