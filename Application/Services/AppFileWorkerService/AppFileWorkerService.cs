using Application.Services.LoggingService;
using Application.Types;
using Domain.Queues.AppFileDtos;
using Microsoft.Extensions.Configuration;
using System.IO.Compression;
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
                var uploadId = Guid.NewGuid().ToString();

                await _utilsService.LogAsync(
                    $"Starting chunked upload with {totalChunks} chunks",
                    ApplicationLogType.Message,
                    ApplicationLogAction.Info,
                    $"AppFileId: {queueItem.AppFileId}, UploadId: {uploadId}, Total Size: {zipFileSize} bytes",
                    traceId
                );

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

                                content.Add(new StringContent(queueItem.AppFileId.ToString()), "AppFileId");
                                content.Add(new StringContent(i.ToString()), "ChunkIndex");
                                content.Add(new StringContent(totalChunks.ToString()), "TotalChunks");
                                content.Add(new StringContent(uploadId), "UploadId");
                                content.Add(new StringContent(uncompressedSize.ToString()), "OriginalFileSize");
                                content.Add(new StringContent(traceId.ToString()), "TraceId");

                                var fileContent = new ByteArrayContent(chunkData);

                                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                                
                                content.Add(fileContent, "ChunkData", $"chunk_{i}.dat");

                                await _utilsService.LogAsync(
                                    $"Uploading chunk {i + 1}/{totalChunks}",
                                    ApplicationLogType.Message,
                                    ApplicationLogAction.Info,
                                    $"Size: {currentChunkSize} bytes, AppFileId: {queueItem.AppFileId}",
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
                                $"AppFileId: {queueItem.AppFileId}, HTTP Status Code: {(int)response.StatusCode}, Response: {jsonResponse}",
                                traceId
                            );
                            
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
