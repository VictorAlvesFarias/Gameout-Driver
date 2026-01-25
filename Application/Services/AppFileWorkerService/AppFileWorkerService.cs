using Application.Dtos;
using Application.Services.LoggingService;
using Application.Types;
using Domain.Queues.AppFileDtos;
using Microsoft.Extensions.Configuration;
using System.IO.Compression;
using System.Text.Json;
using Web.Api.Toolkit.Helpers.Application.Dtos;
using Web.Api.Toolkit.Helpers.Application.Extensions;
using Web.Api.Toolkit.Queues.Application.Services;
using Web.Api.Toolkit.Ws.Application.Contexts;

namespace Application.Services.AppFileWatcherService
{
    public class AppFileWorkerService : IAppFileWorkerService
    {
        private readonly Infrastructure.Context.ApplicationContext _applicationContext;
        private readonly string _apiBaseUrl;
        private readonly string _apiKey;
        private readonly IQueueService<AppFileProcessingQueueItem> _updateQueue;
        private readonly IUtilsService _utilsService;
        private readonly IWebSocketRequestContextAccessor _contextAccessor;
        private readonly IAppFileUtilsService _appFileUtilsService;

        public AppFileWorkerService(
            Infrastructure.Context.ApplicationContext applicationContext,
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
            _utilsService = loggingService;
            _contextAccessor = contextAccessor;
            _appFileUtilsService = utilsService;
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
                    await _utilsService.LogAsync(
                        "Path not found during processing",
                        ApplicationLogType.Message,
                        ApplicationLogAction.Error,
                        $"AppFileId: {queueItem.AppFileId}, Path: {queueItem.Path}",
                        traceId
                    );

                    await _appFileUtilsService.SendAppFileStatus(queueItem.AppFileId, AppFileStatusTypes.PathNotFounded, traceId);

                    return;
                }

                if (!DirectoryAccessible(queueItem.Path))
                {
                    await _utilsService.LogAsync(
                        "Locked files detected during processing",
                        ApplicationLogType.Message,
                        ApplicationLogAction.Error,
                        $"AppFileId: {queueItem.AppFileId}, Path: {queueItem.Path}",
                        traceId
                    );

                    await _appFileUtilsService.SendAppFileStatus(queueItem.AppFileId, AppFileStatusTypes.LockedFiles, traceId);

                    return;
                }

                var tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");
                var directory = new DirectoryInfo(queueItem.Path);
                var files = directory.GetFiles("*", SearchOption.AllDirectories);

                if (files.Length == 0)
                {
                    await _utilsService.LogAsync(
                        "No files found in directory during processing",
                        ApplicationLogType.Message,
                        ApplicationLogAction.Warning,
                        $"AppFileId: {queueItem.AppFileId}, Path: {queueItem.Path}",
                        traceId
                    );

                    await _appFileUtilsService.SendAppFileStatus(queueItem.AppFileId, AppFileStatusTypes.Synced, traceId);
                    
                    return;
                }
                
                using (var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
                {
                    using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false))
                    {
                        foreach (var file in files)
                        {
                            var entryName = Path.GetRelativePath(directory.FullName, file.FullName);
                            archive.CreateEntryFromFile(file.FullName, entryName, CompressionLevel.Optimal);
                        }
                    }
                }

                var zipFileInfo = new FileInfo(tempZipPath);
                var zipFileSize = zipFileInfo.Length;

                await _utilsService.LogAsync(
                    "ZIP file created successfully",
                    ApplicationLogType.Message,
                    ApplicationLogAction.Info,
                    $"Size: {zipFileSize} bytes, Files: {files.Length}, AppFileId: {queueItem.AppFileId}",
                    traceId
                );

                var uncompressedSize = directory.GetDirectorySize();
                var chunkSize = 50 * 1024 * 1024;
                var totalChunks = (int)Math.Ceiling((double)zipFileSize / chunkSize);

                // Primeiro, criar AppStoredFile no backend
                int appStoredFileId = 0;
                using (var httpClient = _utilsService.CreateHttpClient(traceId))
                {
                    var createRequest = new AppStoredFileCreateRequestDto
                    {
                        AppFileId = queueItem.AppFileId,
                        OriginalFileSize = uncompressedSize
                    };

                    var createContent = new StringContent(
                        JsonSerializer.Serialize(createRequest),
                        System.Text.Encoding.UTF8,
                        "application/json"
                    );

                    var createResponse = await httpClient.PostAsync(
                        $"{_apiBaseUrl}/create-app-stored-file",
                        createContent
                    );

                    var createJsonResponse = await createResponse.Content.ReadAsStringAsync();

                    if (!createResponse.IsSuccessStatusCode)
                    {
                        await _utilsService.LogAsync(
                            "Failed to create AppStoredFile",
                            ApplicationLogType.Message,
                            ApplicationLogAction.Error,
                            $"AppFileId: {queueItem.AppFileId}, HTTP Status Code: {(int)createResponse.StatusCode}, Response: {createJsonResponse}",
                            traceId
                        );

                        await _appFileUtilsService.SendAppFileStatus(queueItem.AppFileId, AppFileStatusTypes.Unsynced, traceId);
                        return;
                    }

                    var createResult = JsonSerializer.Deserialize<BaseResponse<AppStoredFileCreateResponseDto>>(
                        createJsonResponse,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );

                    if (createResult?.Data == null)
                    {
                        await _utilsService.LogAsync(
                            "Failed to parse AppStoredFile creation response",
                            ApplicationLogType.Message,
                            ApplicationLogAction.Error,
                            $"AppFileId: {queueItem.AppFileId}, Response: {createJsonResponse}",
                            traceId
                        );

                        await _appFileUtilsService.SendAppFileStatus(queueItem.AppFileId, AppFileStatusTypes.Unsynced, traceId);
                        return;
                    }

                    appStoredFileId = createResult.Data.AppStoredFileId;

                    await _utilsService.LogAsync(
                        $"AppStoredFile created successfully - ID: {appStoredFileId}, DiskFileId: {createResult.Data.DiskFileId}",
                        ApplicationLogType.Message,
                        ApplicationLogAction.Success,
                        $"AppFileId: {queueItem.AppFileId}, Starting chunked upload with {totalChunks} chunks, Total Size: {zipFileSize} bytes",
                        traceId
                    );

                    // Validar que o ID foi criado corretamente
                    if (appStoredFileId <= 0)
                    {
                        await _utilsService.LogAsync(
                            "Invalid AppStoredFileId returned from server",
                            ApplicationLogType.Message,
                            ApplicationLogAction.Error,
                            $"AppFileId: {queueItem.AppFileId}, AppStoredFileId: {appStoredFileId}",
                            traceId
                        );

                        await _appFileUtilsService.SendAppFileStatus(queueItem.AppFileId, AppFileStatusTypes.Unsynced, traceId);
                        return;
                    }
                }

                try
                {
                    using (var httpClient = _utilsService.CreateHttpClient(traceId))
                    {
                        using (var fileStream = new FileStream(tempZipPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true))
                        {
                            for (int i = 0; i < totalChunks; i++)
                            {
                                var offset = i * chunkSize;
                                var currentChunkSize = (int)Math.Min(chunkSize, zipFileSize - offset);
                                var chunkData = new byte[currentChunkSize];
                                
                                fileStream.Seek(offset, SeekOrigin.Begin);

                                await fileStream.ReadAsync(chunkData, 0, currentChunkSize);

                                using var content = new MultipartFormDataContent();

                                content.Add(new StringContent(appStoredFileId.ToString()), "AppStoredFileId");
                                content.Add(new StringContent(i.ToString()), "ChunkIndex");
                                content.Add(new StringContent(totalChunks.ToString()), "TotalChunks");
                                content.Add(new StringContent(traceId.ToString()), "TraceId");

                                var fileContent = new ByteArrayContent(chunkData);

                                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                                
                                content.Add(fileContent, "ChunkData", $"chunk_{i}.dat");

                                await _utilsService.LogAsync(
                                    $"Uploading chunk {i + 1}/{totalChunks}",
                                    ApplicationLogType.Message,
                                    ApplicationLogAction.Info,
                                    $"AppStoredFileId: {appStoredFileId}, Size: {currentChunkSize} bytes, AppFileId: {queueItem.AppFileId}",
                                    traceId
                                );

                                var response = await httpClient.PostAsync(
                                    $"{_apiBaseUrl}/upload-chunk",
                                    content
                                );

                                jsonResponse = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            await _utilsService.LogAsync(
                                $"Failed to upload chunk {i + 1}/{totalChunks}",
                                ApplicationLogType.Message,
                                ApplicationLogAction.Error,
                                $"AppFileId: {queueItem.AppFileId}, AppStoredFileId: {appStoredFileId}, HTTP Status Code: {(int)response.StatusCode}, Response: {jsonResponse}",
                                traceId
                            );
                            
                            try
                            {
                                var statusRequest = new AppStoredFileUpdateStatusRequestDto
                                {
                                    AppStoredFileId = appStoredFileId,
                                    Status = AppStoredFileStatusTypes.Error
                                };

                                var statusContent = new StringContent(
                                    JsonSerializer.Serialize(statusRequest),
                                    System.Text.Encoding.UTF8,
                                    "application/json"
                                );
                                
                                await httpClient.PutAsync(
                                    $"{_apiBaseUrl}/update-app-stored-file-status",
                                    statusContent
                                );
                            }
                            catch (Exception ex)
                            {
                                await _utilsService.LogAsync(
                                    "Failed to update AppStoredFile status to Error",
                                    ApplicationLogType.Exception,
                                    ApplicationLogAction.Error,
                                    $"AppStoredFileId: {appStoredFileId}, Exception: {ex.Message}",
                                    traceId
                                );
                            }
                            
                            await _appFileUtilsService.SendAppFileStatus(queueItem.AppFileId, AppFileStatusTypes.Unsynced, traceId);
                            
                            return;
                        }

                        await _utilsService.LogAsync(
                            $"Chunk {i + 1}/{totalChunks} uploaded successfully",
                            ApplicationLogType.Message,
                            ApplicationLogAction.Success,
                            $"AppFileId: {queueItem.AppFileId}",
                            traceId
                        );
                    }
                        }
                    }

                    await _utilsService.LogAsync(
                        "All chunks uploaded successfully",
                        ApplicationLogType.Message,
                        ApplicationLogAction.Success,
                        $"AppFileId: {queueItem.AppFileId}, Total Chunks: {totalChunks}",
                        traceId
                    );
                }
                finally
                {
                    if (File.Exists(tempZipPath))
                    {
                        try
                        {
                            File.Delete(tempZipPath);
                 
                            await _utilsService.LogAsync(
                                "Temporary ZIP file deleted",
                                ApplicationLogType.Message,
                                ApplicationLogAction.Info,
                                $"Path: {tempZipPath}",
                                traceId
                            );
                        }
                        catch (Exception deleteEx)
                        {
                            await _utilsService.LogAsync(
                                "Failed to delete temporary ZIP file",
                                ApplicationLogType.Exception,
                                ApplicationLogAction.Warning,
                                $"Path: {tempZipPath}, Exception: {deleteEx.Message}",
                                traceId
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await _utilsService.LogAsync(
                    "Exception during synchronization processing",
                    ApplicationLogType.Exception,
                    ApplicationLogAction.Error,
                    $"AppFileId: {queueItem.AppFileId}, Path: {queueItem.Path}, Exception Type: {ex.GetType().Name}, Message: {ex.Message}, Response: {jsonResponse}, StackTrace: {ex.StackTrace}",
                    traceId
                );
                await _appFileUtilsService.SendAppFileStatus(queueItem.AppFileId, AppFileStatusTypes.Unsynced, traceId);
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
    }
}
