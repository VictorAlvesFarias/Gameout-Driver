using Application.Dtos;
using Application.Dtos.AppFile;
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

namespace Application.Services.AppFileWatcherService
{
    public class AppFileWatcherService : IAppFileWatcherService
    {
        private readonly ApplicationContext _applicationContext;
        private readonly HttpClient _httpClient;
        private readonly string _apiBaseUrl = "https://localhost:7000";
        private readonly string _apiKey;
        private readonly IQueueService<AppFileUpdateRequestMessage> _updateQueue;

        public AppFileWatcherService(
            ApplicationContext applicationContext,
            IConfiguration configuration,
            IQueueService<AppFileUpdateRequestMessage> updateQueue
        )
        {
            _applicationContext = applicationContext;
            _apiKey = configuration["ApiKey"] ?? string.Empty;
            _httpClient = new HttpClient();
            _updateQueue = updateQueue;

            if (!string.IsNullOrWhiteSpace(_apiKey))
                _httpClient.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
        }

        private async Task SendAppFileStatus(int id, AppFileStatusTypes status, string message, string details)
        {
            var dto = new UpdateAppFileStatusRequestDto
            {
                AppFileId = id,
                Status = status,
                StatusMessage = message,
                StatusDetails = details
            };

            var json = JsonSerializer.Serialize(dto);

            await _httpClient.PutAsync(
                $"{_apiBaseUrl}/update-appfile-status",
                new StringContent(json, Encoding.UTF8, "application/json")
            );
        }

        private async Task SendAppStoredFileStatus(int id, AppStoredFileStatusTypes status, string message, string details)
        {
            var dto = new UpdateAppStoredFileStatusRequestDto
            {
                AppStoredFileId = id,
                Status = status,
                StatusMessage = message,
                StatusDetails = details
            };

            var json = JsonSerializer.Serialize(dto);

            await _httpClient.PutAsync(
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
            catch
            {
                return false;
            }
        }

        public void SetWatchers()
        {
            var jsonResponse = "";

            try
            {
                var response = _httpClient.GetAsync($"{_apiBaseUrl}/get-files").Result;
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
                                await SendAppFileStatus(appFile.Id, AppFileStatusTypes.Unsynced,"Unsynced","");
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
                SendAppFileStatus(0, AppFileStatusTypes.Error, ex.Message, jsonResponse + ex.StackTrace).Wait();
            }
        }

        public async Task IsProcessing(AppFileStatusCheckRequestMessage body)
        {
            var isInQueue = _updateQueue.Contains(e => e.AppStoredFileId == body.AppStoredFileId);

            if (!isInQueue)
            {
                await this.SendAppStoredFileStatus(body.AppStoredFileId, AppStoredFileStatusTypes.Complete, "Complete", "The queue not contains file to be proccessed.");
            }
            else if (!Directory.Exists(body.Path))
            {
                await this.SendAppStoredFileStatus(body.AppStoredFileId, AppStoredFileStatusTypes.PendingWithError, "Path does not exist", body.Path);
            }
            else if (!DirectoryAccessible(body.Path))
            {
                await this.SendAppStoredFileStatus(body.AppStoredFileId, AppStoredFileStatusTypes.PendingWithError, "Path locked or in use", body.Path);
            }
        }

        public async Task ProcessSingleSync(int appStoredFileId, string path)
        {
            var jsonResponse = "";

            try
            {
                if (!Directory.Exists(path))
                {
                    await SendAppStoredFileStatus(appStoredFileId, AppStoredFileStatusTypes.Error, "Path does not exist", path);
                    return;
                }

                if (!DirectoryAccessible(path))
                {
                    await SendAppStoredFileStatus(appStoredFileId, AppStoredFileStatusTypes.Error, "Path locked or in use", path);
                    return;
                }

                using var memoryStream = new MemoryStream();
                using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true);

                var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    archive.CreateEntryFromFile(file, Path.GetRelativePath(path, file));
                }

                memoryStream.Seek(0, SeekOrigin.Begin);

                var uncompressedSize = GetDirectorySize(files);

                using var content = new MultipartFormDataContent();

                content.Add(new StringContent(appStoredFileId.ToString()), "appStoredFileId");
                content.Add(new StringContent(uncompressedSize.ToString()), "originalFileSize");

                var fileContent = new ByteArrayContent(memoryStream.ToArray());
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");

                content.Add(fileContent, "file", "archive.zip");

                var response = await _httpClient.PostAsync(
                    $"{_apiBaseUrl}/stream-file",
                    content
                );

                jsonResponse = await response.Content.ReadAsStringAsync();

                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                await SendAppStoredFileStatus(appStoredFileId, AppStoredFileStatusTypes.Error, ex.Message, jsonResponse + ex.StackTrace);
            }
        }

        private async void RequestSync(int appFileId)
        {
            var jsonResponse = "";

            try
            {
                var body = new AppFileSyncRequestDto { IdAppFile = appFileId };
                var json = JsonSerializer.Serialize(body);

                var response = await _httpClient.PostAsync(
                    $"{_apiBaseUrl}/request-sync",
                    new StringContent(json, Encoding.UTF8, "application/json")
                );

                jsonResponse = await response.Content.ReadAsStringAsync();

                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                await SendAppFileStatus(appFileId, AppFileStatusTypes.Error, ex.Message, jsonResponse + ex.StackTrace);
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
                catch { }
            }

            return size;
        }

        public void SingleSync(AppFileUpdateRequestMessage body)
        {
            _updateQueue.EnqueueAsync(body);
        }
    }
}
