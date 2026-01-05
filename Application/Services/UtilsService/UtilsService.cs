using Application.Types;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;
using Web.Api.Toolkit.Helpers.Application.Dtos;
using Web.Api.Toolkit.Ws.Application.Contexts;
using Web.Api.Toolkit.Ws.Application.Dtos;

namespace Application.Services.LoggingService
{
    public class UtilsService : IUtilsService
    {
        private readonly string _apiBaseUrl;
        private readonly string _apiKey;
        private readonly IWebSocketRequestContextAccessor _contextAccessor;

        public UtilsService(IConfiguration configuration, IWebSocketRequestContextAccessor contextAccessor)
        {
            _apiBaseUrl = configuration["BackendApi:BaseUrl"] ?? "https://localhost:7000";
            _apiKey = configuration["ApiKey"] ?? string.Empty;
            _contextAccessor = contextAccessor;
        }

        public HttpClient CreateHttpClient(string traceId = "")
        {
            var httpClient = new HttpClient();

            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
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

                var json = JsonSerializer.Serialize(logDto);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var httpClient = CreateHttpClient(traceId);
                await httpClient.PostAsync($"{_apiBaseUrl}/api/application-log/add", content);
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
                var response = httpClient.GetAsync($"{_apiBaseUrl}/get-trace-id").Result;
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

