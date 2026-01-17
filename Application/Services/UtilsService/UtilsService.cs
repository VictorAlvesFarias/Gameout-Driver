using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
using Application.Types;
using System.Text;

namespace Application.Services.LoggingService
{
    public class UtilsService : IUtilsService
    {
        private readonly IConfiguration _configuration;
        private readonly IWebSocketRequestContextAccessor _contextAccessor;

        public UtilsService(IConfiguration configuration, IWebSocketRequestContextAccessor contextAccessor)
        {
            _contextAccessor = contextAccessor;
            _configuration = configuration;
        }

        public HttpClient CreateHttpClient(string traceId = "")
        {
            var backendConfiguration = _configuration.GetSection("BackendApi").Get<BackendApiConfiguration>();
            var webSocketConfiguration = _configuration.GetSection("WebSocket").Get<WebSocketConfiguration>();
            var httpClient = new HttpClient();

            if (!string.IsNullOrWhiteSpace(backendConfiguration.ApiKey))
            {
                httpClient.DefaultRequestHeaders.Add("X-API-Key", backendConfiguration.ApiKey);
            }

            if (!string.IsNullOrWhiteSpace(traceId))
            {
                httpClient.DefaultRequestHeaders.Add("X-Trace-Application-Id", traceId);
            }

            return httpClient;
        }

        public async Task LogAsync(string message, ApplicationLogType type, ApplicationLogAction action, string details, string traceId)
        {
            try
            {
                var logDto = new
                {
                    Message = message,
                    Details = details ?? string.Empty,
                    Type = (int)type,
                    Action = (int)action
                };
                var backendConfiguration = _configuration.GetSection("BackendApi").Get<BackendApiConfiguration>();
                var json = JsonSerializer.Serialize(logDto);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var httpClient = CreateHttpClient(traceId);
                await httpClient.PostAsync($"{backendConfiguration.BaseUrl}/api/application-log/add", content);
            }
            catch
            {
                // Silenciosamente falhar para não quebrar o fluxo principal
            }
        }

        public string GetTraceId(bool onCreate = false)
        {
            var context = _contextAccessor.Context;

            if (context != null)
            {
                var wsRequest = JsonSerializer.Deserialize<WebSocketRequest>(context.Request);

                if (wsRequest?.Headers != null && wsRequest.Headers.TryGetValue("X-Trace-Application-Id", out var traceIdValue))
                {
                    return traceIdValue;
                }
            }

            if (onCreate == false)
            {
                return "";
            }

            using (var httpClient = CreateHttpClient())
            {
                var backendConfiguration = _configuration.GetSection("BackendApi").Get<BackendApiConfiguration>();
                var response = httpClient.GetAsync($"{backendConfiguration.BaseUrl}/get-trace-id").Result;
                var jsonResponse = response.Content.ReadAsStringAsync().Result;
                var body = JsonSerializer.Deserialize<BaseResponse<int>>(
                    jsonResponse,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (body?.Success == true && body.Data > 0)
                {
                    return body.Data.ToString();
                }
            }

            throw new Exception("Error on generating trace id.");
        }
    }
}

