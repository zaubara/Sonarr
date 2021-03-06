﻿using System;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Notifications.MediaBrowser
{
    public class MediaBrowserProxy
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public MediaBrowserProxy(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public void Notify(MediaBrowserSettings settings, string title, string message)
        {
            var path = "/Notifications/Admin";
            var request = BuildRequest(path, settings);

            request.Body = new
                           {
                               Name = title,
                               Description = message,
                               ImageUrl = "https://raw.github.com/NzbDrone/NzbDrone/develop/Logo/64.png"
                           }.ToJson();

            request.Headers.ContentType = "application/json";

            ProcessRequest(request, settings);
        }

        public void Update(MediaBrowserSettings settings, Int32 tvdbId)
        {
            var path = String.Format("/Library/Series/Updated?tvdbid={0}", tvdbId);            
            var request = BuildRequest(path, settings);

            ProcessRequest(request, settings);
        }

        private String ProcessRequest(HttpRequest request, MediaBrowserSettings settings)
        {
            request.Headers.Add("X-MediaBrowser-Token", settings.ApiKey);

            var response = _httpClient.Post(request);
            _logger.Trace("Response: {0}", response.Content);

            CheckForError(response);

            return response.Content;
        }

        private HttpRequest BuildRequest(string path, MediaBrowserSettings settings)
        {
            var url = String.Format(@"http://{0}/mediabrowser", settings.Address);
            
            return new HttpRequestBuilder(url).Build(path);
        }

        private void CheckForError(HttpResponse response)
        {
            _logger.Debug("Looking for error in response: {0}", response);

            //TODO: actually check for the error
        }
    }
}
