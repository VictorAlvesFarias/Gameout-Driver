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
    public class AppFileWatcherService : IAppFileWatcherService
    {
        private readonly ApplicationContext _applicationContext;
        private readonly string _apiBaseUrl;
        private readonly string _apiKey;
        private readonly IQueueService<AppFileProcessingQueueItem> _updateQueue;
        private readonly IUtilsService _loggingService;
        private readonly IWebSocketRequestContextAccessor _contextAccessor;

        public AppFileWatcherService(
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

        private async Task SendAppFileStatus(int id, AppFileStatusTypes status)
        {
            var traceId = _loggingService.GetTraceId();
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
                var s = await response.Content.ReadAsStringAsync();
            }

        }

        private async Task SendAppStoredFileStatus(int id, AppStoredFileStatusTypes status)
        {
            var traceId = _loggingService.GetTraceId();
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
                var s = await response.Content.ReadAsStringAsync();
            }
        }

        private bool DirectoryAccessible(string path)
        {
            var traceId = _loggingService.GetTraceId();

            try
            {
                var files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);

                foreach (var file in files)
                {
                    using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                }

                return true;
            }
            catch (Exception ex)
            {
                _loggingService.LogAsync(
                    $"Directory not accessible: {path}. Error: {ex.Message}",
                    ApplicationLogType.Exception,
                    ApplicationLogAction.Error,
                    ex.StackTrace,
                    traceId

                ).Wait();
                return false;
            }
        }

        public async void SetWatchers()
        {
            var traceId = _loggingService.GetTraceId();
            var jsonResponse = string.Empty;

            using (var httpClient = _loggingService.CreateHttpClient(traceId))
            {
                var response = httpClient.GetAsync($"{_apiBaseUrl}/get-files").Result;

                jsonResponse = response.Content.ReadAsStringAsync().Result;
                    
                response.EnsureSuccessStatusCode();
            }

            var body = JsonSerializer.Deserialize<BaseResponse<List<AppFileResponseDto>>>(
                jsonResponse,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            var watchers = _applicationContext.AppFileWatchers;

            foreach (var appFile in body.Data)
            {
                var watcher = watchers.FirstOrDefault(e => e.AppFileId == appFile.Id);

                if (watcher is not null)
                {
                    continue;
                }

                if (!Directory.Exists(appFile.Path))
                {
                    SendAppFileStatus(appFile.Id, AppFileStatusTypes.Unsynced).Wait();

                    continue;
                }

                if (!DirectoryAccessible(appFile.Path))
                {
                    SendAppFileStatus(appFile.Id, AppFileStatusTypes.Unsynced).Wait();

                    continue;
                }

                watcher = new AppFileWatcher { AppFileId = appFile.Id };

                watcher.FileSystemWatcher = new FileSystemWatcher(appFile.Path)
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                watcher.FileSystemWatcher.Changed += async (s, e) =>
                {
                    var traceId = _loggingService.GetTraceId(true);

                    watcher.FileSystemWatcher.EnableRaisingEvents = false;

                    if (!this.DirectoryAccessible(appFile.Path))
                    {
                        this.SendAppFileStatus(appFile.Id, AppFileStatusTypes.Unsynced).Wait();
                    }
                    else
                    {
                        if (appFile.Observer)
                        {
                            this.RequestSync(appFile.Id);
                        }

                        if (appFile.AutoValidateSync)
                        {
                            await this.SendAppFileStatus(appFile.Id, AppFileStatusTypes.Unsynced);
                        }
                    }

                    Task.Delay(1000).ContinueWith(_ =>
                    {
                        watcher.FileSystemWatcher.EnableRaisingEvents = true;
                    });
                };

                _applicationContext.AppFileWatchers.Add(watcher);
            }

            var idsToRemove = watchers
                .Where(w => !body.Data.Any(a => a.Id == w.AppFileId))
                .Select(w => w.AppFileId)
                .ToList();

            foreach (var id in idsToRemove)
            {
                var watcher = watchers.First(w => w.AppFileId == id);
                watcher.FileSystemWatcher.EnableRaisingEvents = false;
                watcher.FileSystemWatcher.Dispose();
                watchers.Remove(watcher);
            }
        }

        public async Task IsProcessing(AppFileStatusCheckRequestMessage body)
        {
            var traceId = _loggingService.GetTraceId();
            var isInQueue = _updateQueue.Contains(e => e.AppStoredFileId == body.AppStoredFileId);

            if (!isInQueue)
            {
                await this.SendAppStoredFileStatus(body.AppStoredFileId, AppStoredFileStatusTypes.Complete);
            }
            else if (!Directory.Exists(body.Path))
            {
                await this.SendAppStoredFileStatus(body.AppStoredFileId, AppStoredFileStatusTypes.PendingWithError);
            }
            else if (!DirectoryAccessible(body.Path))
            {
                await this.SendAppStoredFileStatus(body.AppStoredFileId, AppStoredFileStatusTypes.PendingWithError);
            }
        }

        public async Task ProcessSingleSync(AppFileProcessingQueueItem queueItem)
        {
            var jsonResponse = "";
            var traceId = queueItem.TraceId;

            try
            {
                if (!Directory.Exists(queueItem.Path))
                {
                    await SendAppStoredFileStatus(queueItem.AppStoredFileId, AppStoredFileStatusTypes.Error);

                    return;
                }

                if (!DirectoryAccessible(queueItem.Path))
                {
                    await SendAppStoredFileStatus(queueItem.AppStoredFileId, AppStoredFileStatusTypes.Error);

                    return;
                }

                using var memoryStream = new MemoryStream();
                using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true);

                var files = Directory.GetFiles(queueItem.Path, "*", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    archive.CreateEntryFromFile(file, Path.GetRelativePath(queueItem.Path, file));
                }

                memoryStream.Seek(0, SeekOrigin.Begin);

                var uncompressedSize = GetDirectorySize(files);

                using var content = new MultipartFormDataContent();

                content.Add(new StringContent(queueItem.AppStoredFileId.ToString()), "appStoredFileId");
                content.Add(new StringContent(uncompressedSize.ToString()), "originalFileSize");

                var fileContent = new ByteArrayContent(memoryStream.ToArray());
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");

                content.Add(fileContent, "file", "archive.zip");

                using (var httpClient = _loggingService.CreateHttpClient(traceId))
                {

                    var response = await httpClient.PostAsync(
                        $"{_apiBaseUrl}/stream-file",
                        content
                    );

                    jsonResponse = await response.Content.ReadAsStringAsync();

                    response.EnsureSuccessStatusCode();
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogAsync(
                    $"Error in ProcessSingleSync for AppStoredFileId {queueItem.AppStoredFileId}: {ex.Message}",
                    ApplicationLogType.Exception,
                    ApplicationLogAction.Error,
                    jsonResponse + "\n" + ex.StackTrace,
                    traceId
                );
                await SendAppStoredFileStatus(queueItem.AppStoredFileId, AppStoredFileStatusTypes.Error);
            }
        }

        private async void RequestSync(int appFileId)
        {
            var traceId = _loggingService.GetTraceId(true);
            var jsonResponse = string.Empty;

            try
            {
                var body = new AppFileSyncRequestDto { IdAppFile = appFileId };
                var json = JsonSerializer.Serialize(body);

                using (var httpClient = _loggingService.CreateHttpClient(traceId))
                {
                    var response = await httpClient.PostAsync(
                        $"{_apiBaseUrl}/request-sync",
                        new StringContent(json, Encoding.UTF8, "application/json")
                    );

                    jsonResponse = await response.Content.ReadAsStringAsync();

                    response.EnsureSuccessStatusCode();
                }

            }
            catch (Exception ex)
            {
                await _loggingService.LogAsync(
                    $"Error in RequestSync for AppFileId {appFileId}: {ex.Message}",
                    ApplicationLogType.Exception,
                    ApplicationLogAction.Error,
                    jsonResponse + "\n" + ex.StackTrace,
                    traceId
                );
                await SendAppFileStatus(appFileId, AppFileStatusTypes.Unsynced);
            }
        }

        public long GetDirectorySize(IEnumerable<string> files)
        {
            long size = 0;

            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    size += info.Length;
                }
                catch (Exception ex)
                {
                    _loggingService.LogAsync(
                        $"Error getting file size: {file}. Error: {ex.Message}",
                        ApplicationLogType.Exception,
                        ApplicationLogAction.Error,
                        ex.StackTrace ?? "",
                        ""
                    ).Wait();
                }
            }

            return size;
        }

        public void SingleSync(AppFileUpdateRequestMessage body)
        {
            var traceId = _loggingService.GetTraceId();
            var queueItem = new AppFileProcessingQueueItem
            {
                AppStoredFileId = body.AppStoredFileId,
                AppFileId = body.AppFileId,
                Path = body.Path,
                TraceId = traceId
            };

            _updateQueue.EnqueueAsync(queueItem);
        }

        public async Task CheckAppFileStatusAll(AppFileStatusCheckAllRequestMessage body)
        {
            var traceId = _loggingService.GetTraceId();
            var hasFilesInProcessing = _updateQueue.Contains(e => e.AppFileId == body.AppFileId);

            if (hasFilesInProcessing)
            {
                await SendAppFileStatus(body.AppFileId, AppFileStatusTypes.InProgress);
            }

            DateTime? mostRecentDate = null;
            long currentFolderSize = 0;

            try
            {
                if (Directory.Exists(body.Path))
                {
                    var files = Directory.GetFiles(body.Path, "*", SearchOption.AllDirectories);

                    currentFolderSize = GetDirectorySize(files);
                        
                    if (files.Length > 0)
                    {
                        mostRecentDate = files.Select(f => File.GetLastWriteTime(f)).Max();
                    }
                }
                else
                {
                    await SendAppFileStatus(body.AppFileId, AppFileStatusTypes.Unsynced);

                    return;
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogAsync(
                    $"Error calculating folder stats for path {body.Path}",
                    ApplicationLogType.Exception,
                    ApplicationLogAction.Warning,
                    ex.Message,
                    traceId
                );

                await SendAppFileStatus(body.AppFileId, AppFileStatusTypes.Unsynced);

                return;
            }

            if (body.LastSyncedFileSize.HasValue && body.LastSyncedFileSize.Value != currentFolderSize)
            {
                await SendAppFileStatus(body.AppFileId, AppFileStatusTypes.Unsynced);

                return;
            }
            else if (body.LastSyncedFileDate.HasValue && mostRecentDate.HasValue && mostRecentDate.Value > body.LastSyncedFileDate.Value)
            {
                await SendAppFileStatus(body.AppFileId, AppFileStatusTypes.Unsynced);

                return;
            }

            await SendAppFileStatus(body.AppFileId, AppFileStatusTypes.Synced);
        }
    }
}
