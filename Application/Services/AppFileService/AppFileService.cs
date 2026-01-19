using Application.Configuration;
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
    public class AppFileService : IAppFileService
    {
        private readonly ApplicationContext _applicationContext;
        private readonly IQueueService<AppFileProcessingQueueItem> _updateQueue;
        private readonly IUtilsService _loggingService;
        private readonly IConfiguration _configuration;
        private readonly IAppFileUtilsService _utilsService;
        private bool _disposed = false;

        public AppFileService(
            ApplicationContext applicationContext,
            IConfiguration configuration,
            IQueueService<AppFileProcessingQueueItem> updateQueue,
            IUtilsService loggingService,
            IWebSocketRequestContextAccessor contextAccessor,
            IAppFileUtilsService utilsService
        )
        {
            _applicationContext = applicationContext;
            _updateQueue = updateQueue;
            _loggingService = loggingService;
            _utilsService = utilsService;
            _configuration = configuration;
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
                    "Directory not accessible",
                    ApplicationLogType.Exception,
                    ApplicationLogAction.Error,
                    $"Path: {path}, Exception Type: {ex.GetType().Name}, Message: {ex.Message}, StackTrace: {ex.StackTrace}",
                    traceId

                ).Wait();
                return false;
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
                var backendConfiguration = _configuration.GetSection("BackendApi").Get<BackendApiConfiguration>();

                using (var httpClient = _loggingService.CreateHttpClient(traceId))
                {
                    var response = await httpClient.PostAsync(
                        $"{backendConfiguration.BaseUrl}/request-sync",
                        new StringContent(json, Encoding.UTF8, "application/json")
                    );

                    jsonResponse = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        await _loggingService.LogAsync(
                            "Failed to request synchronization",
                            ApplicationLogType.Message,
                            ApplicationLogAction.Error,
                            $"AppFileId: {appFileId}, HTTP Status Code: {(int)response.StatusCode}, Response: {jsonResponse}",
                            traceId
                        );
                        await _utilsService.SendAppFileStatus(appFileId, AppFileStatusTypes.Unsynced, traceId);
                        return;
                    }
                }

            }
            catch (Exception ex)
            {
                await _loggingService.LogAsync(
                    "Exception during synchronization request",
                    ApplicationLogType.Exception,
                    ApplicationLogAction.Error,
                    $"AppFileId: {appFileId}, Exception Type: {ex.GetType().Name}, Message: {ex.Message}, Response: {jsonResponse}, StackTrace: {ex.StackTrace}",
                    traceId
                );
                await _utilsService.SendAppFileStatus(appFileId, AppFileStatusTypes.Unsynced, traceId);
            }
        }

        public async void SetWatchers()
        {
            var traceId = _loggingService.GetTraceId();
            var jsonResponse = string.Empty;
            var backendConfiguration = _configuration.GetSection("BackendApi").Get<BackendApiConfiguration>();

            try
            {
                using (var httpClient = _loggingService.CreateHttpClient(traceId))
                {
                    var response = httpClient.GetAsync($"{backendConfiguration.BaseUrl}/get-files").Result;

                    jsonResponse = response.Content.ReadAsStringAsync().Result;
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        await _loggingService.LogAsync(
                            "Failed to fetch files from backend",
                            ApplicationLogType.Message,
                            ApplicationLogAction.Error,
                            $"HTTP Status Code: {(int)response.StatusCode}, Response: {jsonResponse}",
                            traceId
                        );
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogAsync(
                    "Exception while fetching files from backend",
                    ApplicationLogType.Exception,
                    ApplicationLogAction.Error,
                    $"Exception Type: {ex.GetType().Name}, Message: {ex.Message}, StackTrace: {ex.StackTrace}",
                    traceId
                );
                return;
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
                    watcher.FileSystemWatcher?.Dispose();
                    _applicationContext.AppFileWatchers.Remove(watcher);
                    watcher = null;
                }

                if (!Directory.Exists(appFile.Path))
                {
                    await _loggingService.LogAsync(
                        "Path not found during watcher setup",
                        ApplicationLogType.Message,
                        ApplicationLogAction.Error,
                        $"AppFileId: {appFile.Id}, Path: {appFile.Path}",
                        traceId
                    );

                    continue;
                }

                if (!DirectoryAccessible(appFile.Path))
                {
                    await _loggingService.LogAsync(
                        "Directory not accessible (locked files) during watcher setup",
                        ApplicationLogType.Message,
                        ApplicationLogAction.Error,
                        $"AppFileId: {appFile.Id}, Path: {appFile.Path}",
                        traceId
                    );

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

                    if (!Directory.Exists(appFile.Path))
                    {
                        await _loggingService.LogAsync(
                            "Path not found during watcher setup",
                            ApplicationLogType.Message,
                            ApplicationLogAction.Error,
                            $"AppFileId: {appFile.Id}, Path: {appFile.Path}",
                            traceId
                        );

                        watcher.FileSystemWatcher?.Dispose();
                        _applicationContext.AppFileWatchers.Remove(watcher);
                    }

                    if (!this.DirectoryAccessible(appFile.Path))
                    {
                        await _loggingService.LogAsync(
                            "Directory not accessible detected by watcher",
                            ApplicationLogType.Message,
                            ApplicationLogAction.Warning,
                            $"AppFileId: {appFile.Id}, Path: {appFile.Path}",
                            traceId
                        );

                        watcher.FileSystemWatcher?.Dispose();
                        _applicationContext.AppFileWatchers.Remove(watcher);
                    }
                    else
                    {
                        if (appFile.Observer)
                        {
                            this.RequestSync(appFile.Id);
                        }

                        if (appFile.AutoValidateSync)
                        {
                            await this._utilsService.SendAppFileStatus(appFile.Id, AppFileStatusTypes.Unsynced, traceId);
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
                await this._utilsService.SendAppStoredFileStatus(body.AppStoredFileId, AppStoredFileStatusTypes.Complete, traceId);
            }
            else if (!Directory.Exists(body.Path))
            {
                await _loggingService.LogAsync(
                    "Path not found during status check",
                    ApplicationLogType.Message,
                    ApplicationLogAction.Error,
                    $"AppStoredFileId: {body.AppStoredFileId}, Path: {body.Path}",
                    traceId
                );
                await this._utilsService.SendAppStoredFileStatus(body.AppStoredFileId, AppStoredFileStatusTypes.PathNotFounded, traceId);
            }
            else if (!DirectoryAccessible(body.Path))
            {
                await _loggingService.LogAsync(
                    "Locked files detected during status check",
                    ApplicationLogType.Message,
                    ApplicationLogAction.Error,
                    $"AppStoredFileId: {body.AppStoredFileId}, Path: {body.Path}",
                    traceId
                );
                await this._utilsService.SendAppStoredFileStatus(body.AppStoredFileId, AppStoredFileStatusTypes.LockedFiles, traceId);
            }
        }

        public async Task CheckAppFileStatusAll(AppFileStatusCheckAllRequestMessage body)
        {
            var traceId = _loggingService.GetTraceId();
            var hasFilesInProcessing = _updateQueue.Contains(e => e.AppFileId == body.AppFileId);

            if (hasFilesInProcessing)
            {
                await _utilsService.SendAppFileStatus(body.AppFileId, AppFileStatusTypes.Processing, traceId);
                return;
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
                    await _loggingService.LogAsync(
                        "Path not found during status check",
                        ApplicationLogType.Message,
                        ApplicationLogAction.Error,
                        $"AppFileId: {body.AppFileId}, Path: {body.Path}",
                        traceId
                    );
                    await _utilsService.SendAppFileStatus(body.AppFileId, AppFileStatusTypes.PathNotFounded, traceId);

                    return;
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogAsync(
                    "Exception while calculating folder statistics",
                    ApplicationLogType.Exception,
                    ApplicationLogAction.Warning,
                    $"AppFileId: {body.AppFileId}, Path: {body.Path}, Exception Type: {ex.GetType().Name}, Message: {ex.Message}, StackTrace: {ex.StackTrace}",
                    traceId
                );

                await _utilsService.SendAppFileStatus(body.AppFileId, AppFileStatusTypes.Unsynced, traceId);

                return;
            }

            if (body.LastSyncedFileSize != currentFolderSize)
            {
                await _utilsService.SendAppFileStatus(body.AppFileId, AppFileStatusTypes.Unsynced, traceId);

                return;
            }
            else if (body.LastSyncedFileDate < mostRecentDate)
            {
                await _utilsService.SendAppFileStatus(body.AppFileId, AppFileStatusTypes.Unsynced, traceId);

                return;
            }

            await _utilsService.SendAppFileStatus(body.AppFileId, AppFileStatusTypes.Synced, traceId);
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
                        "Error getting file size",
                        ApplicationLogType.Exception,
                        ApplicationLogAction.Error,
                        $"File: {file}, Exception Type: {ex.GetType().Name}, Message: {ex.Message}, StackTrace: {ex.StackTrace}",
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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Liberar todos os FileSystemWatchers
                var watchers = _applicationContext.AppFileWatchers?.ToList();
                if (watchers != null)
                {
                    foreach (var watcher in watchers)
                    {
                        try
                        {
                            if (watcher.FileSystemWatcher != null)
                            {
                                watcher.FileSystemWatcher.EnableRaisingEvents = false;
                                watcher.FileSystemWatcher.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log mas não interrompe o dispose de outros watchers
                            var traceId = _loggingService.GetTraceId();
                            _loggingService.LogAsync(
                                $"Erro ao liberar FileSystemWatcher para AppFile {watcher.AppFileId}: {ex.Message}",
                                ApplicationLogType.Exception,
                                ApplicationLogAction.Error,
                                ex.StackTrace,
                                traceId
                            ).Wait();
                        }
                    }
                    _applicationContext.AppFileWatchers.Clear();
                }
            }

            _disposed = true;
        }
    }
}
