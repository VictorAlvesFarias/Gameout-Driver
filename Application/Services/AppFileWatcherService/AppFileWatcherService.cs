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
        private readonly ILoggingService _loggingService;
        private readonly IWebSocketRequestContextAccessor _contextAccessor;

        public AppFileWatcherService(
            ApplicationContext applicationContext,
            IConfiguration configuration,
            IQueueService<AppFileProcessingQueueItem> updateQueue,
            ILoggingService loggingService,
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

        private HttpClient CreateHttpClient(string? traceId = null)
        {
            var httpClient = new HttpClient();

            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
            }

            if (!string.IsNullOrWhiteSpace(traceId))
            {
                httpClient.DefaultRequestHeaders.Add("X-Trace-Application-Id", traceId);
            }

            return httpClient;
        }

        private string? GetTraceId()
        {
            try
            {
                var context = _contextAccessor.Context;
                if (context != null)
                {
                    var wsRequest = JsonSerializer.Deserialize<WebSocketRequest>(context.Request);
                    if (wsRequest?.Headers != null && wsRequest.Headers.TryGetValue("X-Trace-Application-Id", out var traceIdValue))
                    {
                        return traceIdValue;
                    }
                }
            }
            catch { }
            
            return null;
        }

        private async Task<string?> GetOrCreateTraceId()
        {
            var traceId = GetTraceId();
            
            if (!string.IsNullOrEmpty(traceId))
            {
                return traceId;
            }

            try
            {
                using var httpClient = CreateHttpClient();
                var response = await httpClient.GetAsync($"{_apiBaseUrl}/get-trace-id");
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var body = JsonSerializer.Deserialize<BaseResponse<int>>(
                    jsonResponse,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (body?.Success == true && body.Data > 0)
                {
                    return body.Data.ToString();
                }
            }
            catch (Exception ex)
            {
                await _loggingService.SendErrorLogAsync(
                    $"Error creating trace id: {ex.Message}",
                    "GetOrCreateTraceId",
                    ex.StackTrace ?? ""
                );
            }

            return null;
        }

        private async Task SendAppFileStatus(int id, AppFileStatusTypes status, string message, string details, string? traceId = null)
        {
            var dto = new UpdateAppFileStatusRequestDto
            {
                AppFileId = id,
                Status = status,
                StatusMessage = message,
                StatusDetails = details
            };

            var json = JsonSerializer.Serialize(dto);

            using var httpClient = CreateHttpClient(traceId);
            await httpClient.PutAsync(
                $"{_apiBaseUrl}/update-appfile-status",
                new StringContent(json, Encoding.UTF8, "application/json")
            );
        }

        private async Task SendAppStoredFileStatus(int id, AppStoredFileStatusTypes status, string message, string details, string? traceId = null)
        {
            var dto = new UpdateAppStoredFileStatusRequestDto
            {
                AppStoredFileId = id,
                Status = status,
                StatusMessage = message,
                StatusDetails = details
            };

            var json = JsonSerializer.Serialize(dto);

            using var httpClient = CreateHttpClient(traceId);
            await httpClient.PutAsync(
                $"{_apiBaseUrl}/update-appstoredfile-status",
                new StringContent(json, Encoding.UTF8, "application/json")
            );
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
            catch (Exception ex)
            {
                _loggingService.SendErrorLogAsync(
                    $"Directory not accessible: {path}. Error: {ex.Message}",
                    "DirectoryAccessible",
                    ex.StackTrace ?? ""
                ).Wait();
                return false;
            }
        }

        public void SetWatchers()
        {
            var jsonResponse = "";

            try
            {
                using var httpClient = CreateHttpClient();
                var response = httpClient.GetAsync($"{_apiBaseUrl}/get-files").Result;
                jsonResponse = response.Content.ReadAsStringAsync().Result;

                var body = JsonSerializer.Deserialize<BaseResponse<List<AppFileResponseDto>>>(
                    jsonResponse,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                response.EnsureSuccessStatusCode();

                var watchers = _applicationContext.AppFileWatchers;

                foreach (var appFile in body.Data)
                {
                    var watcher = watchers.FirstOrDefault(e => e.AppFileId == appFile.Id);

                    if (watcher is not null)
                        continue;

                    if (!Directory.Exists(appFile.Path))
                    {
                        SendAppFileStatus(appFile.Id, AppFileStatusTypes.Error, "Path does not exist", appFile.Path).Wait();
                        continue;
                    }

                    if (!DirectoryAccessible(appFile.Path))
                    {
                        SendAppFileStatus(appFile.Id, AppFileStatusTypes.Error, "Path unavailable", appFile.Path).Wait();
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
                        watcher.FileSystemWatcher.EnableRaisingEvents = false;

                        if (!this.DirectoryAccessible(appFile.Path))
                        {
                            this.SendAppFileStatus(appFile.Id, AppFileStatusTypes.Error, "Path locked or in use", appFile.Path).Wait();
                        }
                        else
                        {
                            if (appFile.Observer)
                            {
                                this.RequestSync(appFile.Id);
                            }

                            if (appFile.AutoValidateSync)
                            {
                                await this.SendAppFileStatus(appFile.Id, AppFileStatusTypes.Unsynced,"Unsynced","");
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
            catch (Exception ex)
            {
                _loggingService.SendErrorLogAsync(
                    $"Error in SetWatchers: {ex.Message}",
                    "SetWatchers",
                    jsonResponse + "\n" + ex.StackTrace
                );
                SendAppFileStatus(0, AppFileStatusTypes.Error, ex.Message, jsonResponse + ex.StackTrace).Wait();
            }
        }

        public async Task IsProcessing(AppFileStatusCheckRequestMessage body)
        {
            var traceId = GetTraceId();
            var isInQueue = _updateQueue.Contains(e => e.AppStoredFileId == body.AppStoredFileId);

            if (!isInQueue)
            {
                await this.SendAppStoredFileStatus(body.AppStoredFileId, AppStoredFileStatusTypes.Complete, "Complete", "The queue not contains file to be proccessed.", traceId);
            }
            else if (!Directory.Exists(body.Path))
            {
                await this.SendAppStoredFileStatus(body.AppStoredFileId, AppStoredFileStatusTypes.PendingWithError, "Path does not exist", body.Path, traceId);
            }
            else if (!DirectoryAccessible(body.Path))
            {
                await this.SendAppStoredFileStatus(body.AppStoredFileId, AppStoredFileStatusTypes.PendingWithError, "Path locked or in use", body.Path, traceId);
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
                    await SendAppStoredFileStatus(queueItem.AppStoredFileId, AppStoredFileStatusTypes.Error, "Path does not exist", queueItem.Path, traceId);
                    return;
                }

                if (!DirectoryAccessible(queueItem.Path))
                {
                    await SendAppStoredFileStatus(queueItem.AppStoredFileId, AppStoredFileStatusTypes.Error, "Path locked or in use", queueItem.Path, traceId);
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

                using var httpClient = CreateHttpClient(traceId);
                var response = await httpClient.PostAsync(
                    $"{_apiBaseUrl}/stream-file",
                    content
                );

                jsonResponse = await response.Content.ReadAsStringAsync();

                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                await _loggingService.SendErrorLogAsync(
                    $"Error in ProcessSingleSync for AppStoredFileId {queueItem.AppStoredFileId}: {ex.Message}",
                    "ProcessSingleSync",
                    jsonResponse + "\n" + ex.StackTrace
                );
                await SendAppStoredFileStatus(queueItem.AppStoredFileId, AppStoredFileStatusTypes.Error, ex.Message, jsonResponse + ex.StackTrace, traceId);
            }
        }

        private async void RequestSync(int appFileId)
        {
            var jsonResponse = "";
            var traceId = await GetOrCreateTraceId();

            try
            {
                var body = new AppFileSyncRequestDto { IdAppFile = appFileId };
                var json = JsonSerializer.Serialize(body);

                using var httpClient = CreateHttpClient(traceId);
                var response = await httpClient.PostAsync(
                    $"{_apiBaseUrl}/request-sync",
                    new StringContent(json, Encoding.UTF8, "application/json")
                );

                jsonResponse = await response.Content.ReadAsStringAsync();

                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                await _loggingService.SendErrorLogAsync(
                    $"Error in RequestSync for AppFileId {appFileId}: {ex.Message}",
                    "RequestSync",
                    jsonResponse + "\n" + ex.StackTrace
                );
                await SendAppFileStatus(appFileId, AppFileStatusTypes.Error, ex.Message, jsonResponse + ex.StackTrace, traceId);
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
                    _loggingService.SendErrorLogAsync(
                        $"Error getting file size: {file}. Error: {ex.Message}",
                        "GetDirectorySize",
                        ex.StackTrace ?? ""
                    ).Wait();
                }
            }

            return size;
        }

        public void SingleSync(AppFileUpdateRequestMessage body)
        {
            var traceId = GetTraceId();
            var queueItem = new AppFileProcessingQueueItem
            {
                AppStoredFileId = body.AppStoredFileId,
                Path = body.Path,
                TraceId = traceId
            };
            _updateQueue.EnqueueAsync(queueItem);
        }
    }
}
