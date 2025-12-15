namespace Application.Services.LoggingService
{
    public interface ILoggingService
    {
        Task SendErrorLogAsync(string message, string action, string details = "");
    }
}
