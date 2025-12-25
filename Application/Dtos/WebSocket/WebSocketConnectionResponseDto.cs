namespace Application.Dtos.WebSocket
{
    public class WebSocketConnectionResponseDto
    {
        public bool Success { get; set; }
        public WebSocketConnectionDataDto? Data { get; set; }
    }
}
