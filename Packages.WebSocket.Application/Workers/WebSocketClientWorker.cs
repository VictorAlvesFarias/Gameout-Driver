﻿using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Packages.Ws.Application.Workers
{
    public class WebSocketClientRequest
    {
        public string Event { get; set; }
        public JsonElement? Body { get; set; }
        public T Deserialize<T>() => Body.Value.Deserialize<T>();
    }

    public class WebSocketClientWorker : BackgroundService
    {
        private readonly ILogger<WebSocketClientWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private ClientWebSocket _socket;
        private readonly ConcurrentDictionary<string, Func<WebSocketClientRequest, CancellationToken, Task>> _handlers;
        private readonly TimeSpan _reconnectDelay;

        public WebSocketClientWorker(ILogger<WebSocketClientWorker> logger, IServiceProvider serviceProvider, TimeSpan? reconnectDelay = null)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _handlers = new ConcurrentDictionary<string, Func<WebSocketClientRequest, CancellationToken, Task>>();
            _socket = new ClientWebSocket();
            _reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(3);
        }

        protected virtual string GetUrl() => "ws://localhost:5000/ws";
        protected virtual Dictionary<string, string> GetHeaders() => new();
        protected virtual CookieContainer GetCookies() => new();
        protected virtual TimeSpan GetReconnectDelay() => _reconnectDelay;

        public void Subscribe(string eventType, Func<WebSocketClientRequest, CancellationToken, Task> handler)
        {
            _handlers[eventType] = handler;
        }

        public async Task SendAsync<T>(T payload, CancellationToken token = default)
        {
            if (_socket.State != WebSocketState.Open)
                return;

            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _socket = new ClientWebSocket();
                    var url = GetUrl();

                    foreach (var header in GetHeaders())
                        _socket.Options.SetRequestHeader(header.Key, header.Value);

                    _socket.Options.Cookies = GetCookies();

                    await _socket.ConnectAsync(new Uri(url), stoppingToken);
                    _logger.LogInformation("Cliente WS conectado: {0}", url);

                    await ReceiveLoop(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Erro na conexão WS, reconectando em {0}s...", GetReconnectDelay().TotalSeconds);
                    await Task.Delay(GetReconnectDelay(), stoppingToken);
                }
            }
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            var buffer = new byte[8 * 1024];

            while (_socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                }
                catch
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", token);
                    break;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                ProcessMessage(message, token);
            }
        }

        private void ProcessMessage(string message, CancellationToken token)
        {
            try
            {
                var req = JsonSerializer.Deserialize<WebSocketClientRequest>(message);
                if (req == null || string.IsNullOrWhiteSpace(req.Event))
                    return;

                if (_handlers.TryGetValue(req.Event, out var handler))
                    _ = Task.Run(() => handler(req, token), token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro processando mensagem WS");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_socket != null && _socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Service stopping", cancellationToken);
            }
            catch { }

            _socket?.Dispose();
            await base.StopAsync(cancellationToken);
        }
    }
}
