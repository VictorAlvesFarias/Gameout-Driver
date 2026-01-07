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
        private readonly string _apiBaseUrl;
        private readonly IQueueService<AppFileProcessingQueueItem> _updateQueue;
        private readonly IUtilsService _loggingService;
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
            _apiBaseUrl = configuration["BackendApi:BaseUrl"] ?? "https://localhost:7000";
            _updateQueue = updateQueue;
            _loggingService = loggingService;
            _utilsService = utilsService;
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

                // Se o watcher já existe, remove o antigo para substituir pelo novo
                if (watcher is not null)
                {
                    watcher.FileSystemWatcher?.Dispose();
                    _applicationContext.AppFileWatchers.Remove(watcher);
                    watcher = null;
                }

                if (!Directory.Exists(appFile.Path))
                {
                    _utilsService.SendAppFileStatus(appFile.Id, AppFileStatusTypes.Unsynced, traceId).Wait();

                    continue;
                }

                if (!DirectoryAccessible(appFile.Path))
                {
                    _utilsService.SendAppFileStatus(appFile.Id, AppFileStatusTypes.Unsynced, traceId).Wait();

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
                        this._utilsService.SendAppFileStatus(appFile.Id, AppFileStatusTypes.Unsynced, traceId).Wait();
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
                await this._utilsService.SendAppStoredFileStatus(body.AppStoredFileId, AppStoredFileStatusTypes.PendingWithError, traceId);
            }
            else if (!DirectoryAccessible(body.Path))
            {
                await this._utilsService.SendAppStoredFileStatus(body.AppStoredFileId, AppStoredFileStatusTypes.PendingWithError, traceId);
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
                await _utilsService.SendAppFileStatus(appFileId, AppFileStatusTypes.Unsynced, traceId);
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
                await _utilsService.SendAppFileStatus(body.AppFileId, AppFileStatusTypes.InProgress, traceId);
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
                    await _utilsService.SendAppFileStatus(body.AppFileId, AppFileStatusTypes.Unsynced, traceId);

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

                await _utilsService.SendAppFileStatus(body.AppFileId, AppFileStatusTypes.Unsynced, traceId);

                return;
            }

            if (body.LastSyncedFileSize.HasValue && body.LastSyncedFileSize.Value != currentFolderSize)
            {
                await _utilsService.SendAppFileStatus(body.AppFileId, AppFileStatusTypes.Unsynced, traceId);

                return;
            }
            else if (body.LastSyncedFileDate.HasValue && mostRecentDate.HasValue && mostRecentDate.Value > body.LastSyncedFileDate.Value)
            {
                await _utilsService.SendAppFileStatus(body.AppFileId, AppFileStatusTypes.Unsynced, traceId);

                return;
            }

            await _utilsService.SendAppFileStatus(body.AppFileId, AppFileStatusTypes.Synced, traceId);
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
