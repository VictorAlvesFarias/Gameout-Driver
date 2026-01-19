using Application.Dtos;
using Application.Dtos.AppFile;
using Application.Services.LoggingService;
using Application.Types;
using Domain.Entitites.ApplicationContext;
using Domain.Entitites.ApplicationContextDb;
using Domain.Entitites.Shared;
using Domain.Queues.AppFileDtos;
using Infrastructure.Context;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Web.Api.Toolkit.Helpers.Application.Dtos;
using Web.Api.Toolkit.Queues.Application.Services;
using Web.Api.Toolkit.Ws.Application.Contexts;
using Web.Api.Toolkit.Ws.Application.Dtos;

namespace Application.Services.AppFileWatcherService
{
    public class AppFileUtilsService : IAppFileUtilsService
    {
        private readonly ApplicationContext _applicationContext;
        private readonly string _apiBaseUrl;
        private readonly string _apiKey;
        private readonly IQueueService<AppFileProcessingQueueItem> _updateQueue;
        private readonly IUtilsService _loggingService;
        private readonly IWebSocketRequestContextAccessor _contextAccessor;

        public AppFileUtilsService(
            ApplicationContext applicationContext,
            IConfiguration configuration,
            IQueueService<AppFileProcessingQueueItem> updateQueue,
            IUtilsService loggingService,
            IWebSocketRequestContextAccessor contextAccessor
        )
        {
            _applicationContext = applicationContext;
            _apiKey = configuration["ApiKey"] ?? string.Empty;
            _apiBaseUrl = configuration["BackendApi:BaseUrl"] ?? "https://localhost:7000";
            _updateQueue = updateQueue;
            _loggingService = loggingService;
            _contextAccessor = contextAccessor;
        }

        public async Task SendAppFileStatus(int id, AppFileStatusTypes status, string traceId)
        {
            try
            {
                var dto = new UpdateAppFileStatusRequestDto
                {
                    AppFileId = id,
                    Status = status
                };
                var json = JsonSerializer.Serialize(dto);

                using (var httpClient = _loggingService.CreateHttpClient(traceId))
                {
                    var response = await httpClient.PutAsync(
                        $"{_apiBaseUrl}/update-appfile-status",
                        new StringContent(json, Encoding.UTF8, "application/json")
                    );
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        await _loggingService.LogAsync(
                            "Failed to update AppFile status",
                            ApplicationLogType.Message,
                            ApplicationLogAction.Error,
                            $"AppFileId: {id}, Status: {status}, HTTP Status Code: {(int)response.StatusCode}, Response: {errorContent}",
                            traceId
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogAsync(
                    "Exception while updating AppFile status",
                    ApplicationLogType.Exception,
                    ApplicationLogAction.Error,
                    $"AppFileId: {id}, Status: {status}, Exception Type: {ex.GetType().Name}, Message: {ex.Message}, StackTrace: {ex.StackTrace}",
                    traceId
                );
            }
        }

        public async Task SendAppStoredFileStatus(int id, AppStoredFileStatusTypes status, string traceId)
        {
            try
            {
                var dto = new UpdateAppStoredFileStatusRequestDto
                {
                    AppStoredFileId = id,
                    Status = status
                };
                var json = JsonSerializer.Serialize(dto);

                using (var httpClient = _loggingService.CreateHttpClient(traceId))
                {
                    var response = await httpClient.PutAsync(
                        $"{_apiBaseUrl}/update-appstoredfile-status",
                        new StringContent(json, Encoding.UTF8, "application/json")
                    );
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        await _loggingService.LogAsync(
                            "Failed to update AppStoredFile status",
                            ApplicationLogType.Message,
                            ApplicationLogAction.Error,
                            $"AppStoredFileId: {id}, Status: {status}, HTTP Status Code: {(int)response.StatusCode}, Response: {errorContent}",
                            traceId
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogAsync(
                    "Exception while updating AppStoredFile status",
                    ApplicationLogType.Exception,
                    ApplicationLogAction.Error,
                    $"AppStoredFileId: {id}, Status: {status}, Exception Type: {ex.GetType().Name}, Message: {ex.Message}, StackTrace: {ex.StackTrace}",
                    traceId
                );
            }
        }
    }
}
