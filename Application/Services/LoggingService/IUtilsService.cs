using Application.Types;

namespace Application.Services.LoggingService
{
    public interface IUtilsService
    {
        Task LogAsync(string message, ApplicationLogType type, ApplicationLogAction action, string details, string traceId);
        HttpClient CreateHttpClient(string traceId = "");
        string GetTraceId(bool onCreate = false);
    }
}
