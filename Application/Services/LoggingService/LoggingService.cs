using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace Application.Services.LoggingService
{
    public class LoggingService : ILoggingService
    {
        private readonly string _apiBaseUrl;
        private readonly string _apiKey;

        public LoggingService(IConfiguration configuration)
        {
            _apiBaseUrl = configuration["BackendApi:BaseUrl"] ?? "https://localhost:7000";
            _apiKey = configuration["ApiKey"] ?? string.Empty;
        }

        private HttpClient CreateHttpClient()
        {
            var httpClient = new HttpClient();

            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
            }

            return httpClient;
        }

        public async Task SendErrorLogAsync(string message, string action, string type)
        {
            try
            {
                var logDto = new
                {
                    Message = message,
                    Type = "Error",
                    Action = action
                };

                var json = JsonSerializer.Serialize(logDto);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var httpClient = CreateHttpClient();
                await httpClient.PostAsync($"{_apiBaseUrl}/api/application-log/add", content);
            }
            catch
            {
                // Silenciosamente falhar para não quebrar o fluxo principal
                // em caso de erro ao enviar o log
            }
        }
    }
}
