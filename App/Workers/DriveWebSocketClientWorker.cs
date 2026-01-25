using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;
using Web.Api.Toolkit.Ws.Application.Contexts;
using Web.Api.Toolkit.Ws.Application.Dtos;
using Web.Api.Toolkit.Ws.Application.Workers;
using System.Threading;
using Application.Dtos;
using Application.Configuration;
using Web.Api.Toolkit.Helpers.Application.Dtos;
using System;
using System.Net.Sockets;
using Application.Constants;

namespace App.Workers
{
    public class DriveWebSocketClientWorker : WebSocketClientWorker
    {
        private readonly IOptionsMonitor<WebSocketConfiguration> _wsOptions;
        private readonly IOptionsMonitor<BackendApiConfiguration> _apiOptions;
        private readonly ILogger<DriveWebSocketClientWorker> _logger;

        public DriveWebSocketClientWorker(
            ILogger<DriveWebSocketClientWorker> logger,
            IServiceScopeFactory scopeFactory,
            IOptionsMonitor<WebSocketConfiguration> wsOptions,
            IOptionsMonitor<BackendApiConfiguration> apiOptions
        ) : base(logger, scopeFactory, TimeSpan.FromSeconds(5))
        {
            _logger = logger;
            _wsOptions = wsOptions;
            _apiOptions = apiOptions;
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            MessageBox.Show($"The process of connecting to the server has been completed.", ApplicationConstants.ApplicationName);
        }

        protected override async Task<string> GetUrlAsync()
        {
            var webSocketConfiguration = _wsOptions.CurrentValue;
                
            return await Task.FromResult(webSocketConfiguration.Url);
        }

        protected override CookieContainer GetCookies()
        {
            var cookies = base.GetCookies();
            var webSocketConfiguration = _wsOptions.CurrentValue;
                
            if (webSocketConfiguration == null || string.IsNullOrWhiteSpace(webSocketConfiguration.Url))
            {
                _logger.LogWarning("WebSocket configuration not found, cannot set cookies");
                return cookies;
            }

            var uri = new Uri(webSocketConfiguration.Url);

            cookies.Add(uri, new Cookie("type", "drive"));

            return cookies;
        }

        protected override Dictionary<string, string> GetHeaders()
        {
            var headers = base.GetHeaders();
            var backendConfiguration = _apiOptions.CurrentValue;
                
            if (backendConfiguration == null || string.IsNullOrWhiteSpace(backendConfiguration.ApiKey))
            {
                _logger.LogError("BackendApi configuration not found or ApiKey is empty");
                throw new InvalidOperationException("BackendApi configuration is missing or ApiKey is empty");
            }

            headers.Add("X-API-Key", backendConfiguration.ApiKey);
                
            return headers;
        }

        protected override Task OnDisconnectedAsync(Exception ex)
        {
            if (!this.ReconectRequest)
            {
                MessageBox.Show($"Connection lost with the server.", ApplicationConstants.ApplicationName);
            }


            return Task.CompletedTask;
        }

        protected override TimeSpan GetReconnectDelay()
        {
            return TimeSpan.FromSeconds(5);
        }
    }
}
