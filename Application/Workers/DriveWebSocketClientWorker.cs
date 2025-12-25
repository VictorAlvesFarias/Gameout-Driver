using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using Web.Api.Toolkit.Ws.Application.Contexts;
using Web.Api.Toolkit.Ws.Application.Dtos;
using Web.Api.Toolkit.Ws.Application.Workers;

namespace Application.Workers
{
    public class DriveWebSocketClientWorker : WebSocketClientWorker
    {
        private readonly IConfiguration _configuration;
        private string _connectionUrl = string.Empty;
        private string _connectionToken = string.Empty;
        private readonly string _backendApiBaseUrl;
        private readonly string _apiKey;
        private readonly ILogger<DriveWebSocketClientWorker> _logger;

        public DriveWebSocketClientWorker(
            ILogger<DriveWebSocketClientWorker> logger,
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration
        ) : base(logger, scopeFactory, TimeSpan.FromSeconds(5))
        {
            _logger = logger;
            _configuration = configuration;
            _backendApiBaseUrl = _configuration["BackendApi:BaseUrl"] ?? "https://localhost:7000";
            _apiKey = _configuration["ApiKey"] ?? string.Empty;
        }

        protected override async Task<string> GetUrlAsync()
        {
            try
            {
                using var httpClient = new HttpClient();

                if (!string.IsNullOrWhiteSpace(_apiKey))
                {
                    httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
                }

                _logger.LogInformation("Solicitando token de conexão WebSocket do backend: {BackendUrl}", _backendApiBaseUrl);

                var response = await httpClient.PostAsync($"{_backendApiBaseUrl}/api/websocket/connect", null);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Falha ao obter token de conexão. Status: {StatusCode}", response.StatusCode);
                    
                    return string.Empty;
                }

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<WebSocketConnectionResponse>(content, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                if (data?.Success == true && data.Data != null)
                {
                    _connectionToken = data.Data.Token ?? string.Empty;
                    var urlPath = data.Data.Url ?? "/ws";

                    // Constrói a URL completa do WebSocket
                    var backendUri = new Uri(_backendApiBaseUrl);
                    var protocol = backendUri.Scheme == "https" ? "wss" : "ws";
                    
                    _connectionUrl = $"{protocol}://{backendUri.Host}:{backendUri.Port}{urlPath}";

                    _logger.LogInformation(
                        "Token de conexão WebSocket recebido com sucesso. URL: {Url}, Expira em: {ExpiresAt}", 
                        _connectionUrl, 
                        data.Data.ExpiresAt
                    );

                    return _connectionUrl;
                }
                else
                {
                    _logger.LogError("Resposta inválida do backend ao solicitar token de conexão");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao solicitar token de conexão WebSocket");
                return string.Empty;
            }
        }

        protected override CookieContainer GetCookies()
        {
            var cookies = new CookieContainer();

            if (!string.IsNullOrWhiteSpace(_connectionUrl) && !string.IsNullOrWhiteSpace(_connectionToken))
            {
                var uri = new Uri(_connectionUrl);
                var userId = _configuration["WebSocket:UserId"] ?? string.Empty;

                cookies.Add(uri, new Cookie("x-token-invite", _connectionToken));
                cookies.Add(uri, new Cookie("type", "drive"));
                cookies.Add(uri, new Cookie("id", userId));

                _logger.LogDebug("Cookie x-token-invite adicionado para autenticação WebSocket");
            }
            else
            {
                _logger.LogWarning("Token de conexão não disponível. Cookies não foram adicionados.");
            }

            return cookies;
        }

        protected override TimeSpan GetReconnectDelay()
        {
            return TimeSpan.FromSeconds(5);
        }

        private class WebSocketConnectionResponse
        {
            public bool Success { get; set; }
            public WebSocketConnectionData? Data { get; set; }
        }

        private class WebSocketConnectionData
        {
            public string? Url { get; set; }
            public string? Token { get; set; }
            public DateTime ExpiresAt { get; set; }
        }
    }
}
