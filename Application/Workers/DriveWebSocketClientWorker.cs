using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace Packages.Ws.Application.Workers
{
    public class DriveWebSocketClientWorker : WebSocketClientWorker
    {
        private readonly IConfiguration _configuration;

        public DriveWebSocketClientWorker(
            ILogger<DriveWebSocketClientWorker> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration
        ) : base(logger, serviceProvider, TimeSpan.FromSeconds(5))
        {
            _configuration = configuration;
        }

        protected override string GetUrl()
        {
            return _configuration["WebSocket:Url"] ?? "ws://localhost:8081/ws";
        }

        protected override Dictionary<string, string> GetHeaders()
        {
            var userId = _configuration["WebSocket:UserId"] ?? string.Empty;
            return new Dictionary<string, string>
            {
                { "type", "drive" },
                { "authorization", userId }
            };
        }

        protected override CookieContainer GetCookies()
        {
            var cookies = new CookieContainer();
            var uri = new Uri(GetUrl());
            var userId = _configuration["WebSocket:UserId"] ?? string.Empty;

            cookies.Add(uri, new Cookie("type", "drive"));
            cookies.Add(uri, new Cookie("authentication", userId));

            return cookies;
        }

        protected override TimeSpan GetReconnectDelay()
        {
            return TimeSpan.FromSeconds(5);
        }
    }
}
