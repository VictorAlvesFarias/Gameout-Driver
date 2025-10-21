using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Packages.Ws.Application.Workers
{
    public delegate Task WebSocketClientHandler<T>(T message, CancellationToken token);

    public class WebSocketClientRequest
    {
        public string Event { get; set; }
        public JsonElement? Body { get; set; }

        public T Deserialize<T>() => Body.Value.Deserialize<T>();
    }

    public class WebSocketClientWorker : BackgroundService
    {
        private readonly ILogger<WebSocketClientWorker> _logger;
        private readonly Uri _uri;
        private ClientWebSocket _socket;
        private readonly ConcurrentDictionary<string, Func<WebSocketClientRequest, CancellationToken, Task>> _handlers;

        public WebSocketClientWorker(ILogger<WebSocketClientWorker> logger, string uri)
        {
            _logger = logger;
            _uri = new Uri(uri);
            _socket = new ClientWebSocket();
            _handlers = new ConcurrentDictionary<string, Func<WebSocketClientRequest, CancellationToken, Task>>();
        }

        public void Subscribe(string type, Func<WebSocketClientRequest, CancellationToken, Task> handler)
        {
            _handlers[type] = handler;
        }

        public async Task SendAsync<T>(T payload, CancellationToken token = default)
        {
            if (_socket.State != WebSocketState.Open)
                return;

            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            var seg = new ArraySegment<byte>(bytes);

            try
            {
                await _socket.SendAsync(seg, WebSocketMessageType.Text, true, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro enviando mensagem WS");
            }
        }

        public async Task BroadcastAsync<T>(T payload, Func<T, bool>? filter = null, CancellationToken token = default)
        {
            if (filter != null && !filter(payload))
                return;

            await SendAsync(payload, token);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _socket = new ClientWebSocket();
                    await _socket.ConnectAsync(_uri, stoppingToken);
                    _logger.LogInformation("Cliente WS conectado: {0}", _uri);

                    await ReceiveLoop(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Erro na conexão WS, reconectando em 3s...");
                    await Task.Delay(3000, stoppingToken);
                }
            }
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            var buffer = new byte[4 * 1024];

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
                var wsReq = JsonSerializer.Deserialize<WebSocketClientRequest>(message);
                if (wsReq == null || string.IsNullOrWhiteSpace(wsReq.Event))
                    return;

                if (_handlers.TryGetValue(wsReq.Event, out var handler))
                {
                    _ = Task.Run(() => handler(wsReq, token), token);
                }
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
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Service stopping", cancellationToken);
                }
            }
            catch { }

            _socket?.Dispose();
            await base.StopAsync(cancellationToken);
        }
    }
}
