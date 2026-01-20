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
    public class AppFileWorkerService : IAppFileWorkerService
    {
        private readonly ApplicationContext _applicationContext;
        private readonly string _apiBaseUrl;
        private readonly string _apiKey;
        private readonly IQueueService<AppFileProcessingQueueItem> _updateQueue;
        private readonly IUtilsService _loggingService;
        private readonly IWebSocketRequestContextAccessor _contextAccessor;
        private readonly IAppFileUtilsService _utilsService;

        public AppFileWorkerService(
            ApplicationContext applicationContext,
            IConfiguration configuration,
            IQueueService<AppFileProcessingQueueItem> updateQueue,
            IUtilsService loggingService,
            IWebSocketRequestContextAccessor contextAccessor,
            IAppFileUtilsService utilsService
        )
        {
            _applicationContext = applicationContext;
            _apiKey = configuration["ApiKey"] ?? string.Empty;
            _apiBaseUrl = configuration["BackendApi:BaseUrl"] ?? "https://localhost:7000";
            _updateQueue = updateQueue;
            _loggingService = loggingService;
            _contextAccessor = contextAccessor;
            _utilsService = utilsService;
        }

        public async Task ProcessSingleSync(AppFileProcessingQueueItem queueItem)
        {
            _applicationContext.CurrentAppFileProcessingQueueItem = queueItem;

            var jsonResponse = "";
            var traceId = queueItem.TraceId;

            try
            {
                if (!Directory.Exists(queueItem.Path))
                {
                    await _loggingService.LogAsync(
                        "Path not found during processing",
                        ApplicationLogType.Message,
                        ApplicationLogAction.Error,
                        $"AppFileId: {queueItem.AppFileId}, Path: {queueItem.Path}",
                        traceId
                    );
                    await _utilsService.SendAppFileStatus(queueItem.AppFileId, AppFileStatusTypes.PathNotFounded, traceId);

                    return;
                }

                if (!DirectoryAccessible(queueItem.Path))
                {
                    await _loggingService.LogAsync(
                        "Locked files detected during processing",
                        ApplicationLogType.Message,
                        ApplicationLogAction.Error,
                        $"AppFileId: {queueItem.AppFileId}, Path: {queueItem.Path}",
                        traceId
                    );
                    await _utilsService.SendAppFileStatus(queueItem.AppFileId, AppFileStatusTypes.LockedFiles, traceId);

                    return;
                }

                using var memoryStream = new MemoryStream();
                
                var files = Directory.GetFiles(queueItem.Path, "*", SearchOption.AllDirectories);
                
                if (files.Length == 0)
                {
                    await _loggingService.LogAsync(
                        "No files found in directory during processing",
                        ApplicationLogType.Message,
                        ApplicationLogAction.Warning,
                        $"AppFileId: {queueItem.AppFileId}, Path: {queueItem.Path}",
                        traceId
                    );

                    await _utilsService.SendAppFileStatus(queueItem.AppFileId, AppFileStatusTypes.Unsynced, traceId);
                    
                    return;
                }
                
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var file in files)
                    {
                        var entryName = Path.GetRelativePath(queueItem.Path, file);
                        archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                    }
                }
                
                memoryStream.Seek(0, SeekOrigin.Begin);
                
                var zipBytes = memoryStream.ToArray();

                await _loggingService.LogAsync(
                    "ZIP file created successfully",
                    ApplicationLogType.Message,
                    ApplicationLogAction.Info,
                    $"Size: {zipBytes.Length} bytes, Files: {files.Length}, AppFileId: {queueItem.AppFileId}",
                    traceId
                );

                if (zipBytes.Length == 0)
                {
                    await _loggingService.LogAsync(
                        "Generated ZIP file is empty",
                        ApplicationLogType.Message,
                        ApplicationLogAction.Error,
                        $"AppFileId: {queueItem.AppFileId}, Path: {queueItem.Path}",
                        traceId
                    );
                    await _utilsService.SendAppFileStatus(queueItem.AppFileId, AppFileStatusTypes.Unsynced, traceId);
                    return;
                }

                var uncompressedSize = GetDirectorySize(files);

                using var content = new MultipartFormDataContent();

                content.Add(new StringContent(queueItem.AppFileId.ToString()), "appFileId");
                content.Add(new StringContent(uncompressedSize.ToString()), "originalFileSize");

                var fileContent = new ByteArrayContent(zipBytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");

                content.Add(fileContent, "file", "archive.zip");

                using (var httpClient = _loggingService.CreateHttpClient(traceId))
                {

                    var response = await httpClient.PostAsync(
                        $"{_apiBaseUrl}/stream-file",
                        content
                    );

                    jsonResponse = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        await _loggingService.LogAsync(
                            "Failed to upload file to backend",
                            ApplicationLogType.Message,
                            ApplicationLogAction.Error,
                            $"AppFileId: {queueItem.AppFileId}, Path: {queueItem.Path}, HTTP Status Code: {(int)response.StatusCode}, Response: {jsonResponse}",
                            traceId
                        );
                        await _utilsService.SendAppFileStatus(queueItem.AppFileId, AppFileStatusTypes.Unsynced, traceId);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogAsync(
                    "Exception during synchronization processing",
                    ApplicationLogType.Exception,
                    ApplicationLogAction.Error,
                    $"AppFileId: {queueItem.AppFileId}, Path: {queueItem.Path}, Exception Type: {ex.GetType().Name}, Message: {ex.Message}, Response: {jsonResponse}, StackTrace: {ex.StackTrace}",
                    traceId
                );
                await _utilsService.SendAppFileStatus(queueItem.AppFileId, AppFileStatusTypes.Unsynced, traceId);
            }
            finally
            {
                _applicationContext.CurrentAppFileProcessingQueueItem = null;
            }
        }

        private bool DirectoryAccessible(string path)
        {
            try
            {
                var files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);

                foreach (var file in files)
                {
                    using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private long GetDirectorySize(string[] files)
        {
            long size = 0;

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                size += fileInfo.Length;
            }

            return size;
        }
    }
}
