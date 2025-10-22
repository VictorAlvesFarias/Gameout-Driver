using Domain.Entitites.ApplicationContext;
using Domain.Queues.AppFileDtos;
using Infrastructure.Context;
using Packages.Ws.Application.Workers;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Drivers.Services.AppFileWatcherService
{
    public class AppFileWatcherService : IAppFileWatcherService
    {
        private readonly ApplicationContext _applicationContext;
        private readonly WebSocketClientWorker _webSocketClientWorker;
        private readonly HttpClient _httpClient;
        private readonly string _apiBaseUrl = "https://localhost:5001/api/appfile";

        public AppFileWatcherService(
            ApplicationContext applicationContext,
            WebSocketClientWorker webSocketClientWorker
        )
        {
            _applicationContext = applicationContext;
            _webSocketClientWorker = webSocketClientWorker;
            _httpClient = new HttpClient();
        }

        public void SingleSync(AppFileUpdateRequestMessage req)
        {
            SingleSync(req.AppStoredFileId, req.Path);
        }

        public void SetWatchers(AppFileSetEventsRequestMessage req)
        {
            var watchers = _applicationContext.AppFileWatchers;

            foreach (var appFile in req.AppFiles)
            {
                var watcher = watchers.FirstOrDefault(e => e.AppFileId == appFile.Id);

                if (watcher is not null || !Directory.Exists(appFile.Path))
                    continue;

                watcher = new AppFileWatcher() { AppFileId = appFile.Id };

                watcher.FileSystemWatcher = new FileSystemWatcher(appFile.Path)
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                watcher.FileSystemWatcher.Deleted += (s, e) =>
                {
                    Console.WriteLine($"Arquivo removido: {e.FullPath}");
                };

                watcher.FileSystemWatcher.Changed += (s, e) =>
                {
                    watcher.FileSystemWatcher.EnableRaisingEvents = false;

                    if (appFile.Observer)
                        RequestSync(appFile.Id);

                    if (appFile.AutoValidateSync)
                        ValidateSync(new AppFileValidateStatusRequest { AppFile = appFile });

                    Task.Delay(1000).ContinueWith(_ =>
                    {
                        Console.WriteLine($"Arquivo alterado: {e.FullPath}");
                        watcher.FileSystemWatcher.EnableRaisingEvents = true;
                    });
                };

                watcher.FileSystemWatcher.Renamed += (s, e) =>
                {
                    Console.WriteLine($"Arquivo renomeado: {e.FullPath}");
                };

                _applicationContext.AppFileWatchers.Add(watcher);
            }

            var idsToRemove = _applicationContext.AppFileWatchers
                .Where(w => !req.AppFiles.Any(a => a.Id == w.AppFileId))
                .Select(w => w.AppFileId)
                .ToList();

            foreach (var id in idsToRemove)
            {
                var watcher = _applicationContext.AppFileWatchers.First(w => w.AppFileId == id);
                watcher.FileSystemWatcher.EnableRaisingEvents = false;
                watcher.FileSystemWatcher.Dispose();
                _applicationContext.AppFileWatchers.Remove(watcher);
            }
        }

        public async void IsProcessing(AppFileStatusCheckRequestMessage req)
        {
            var response = new AppFileStatusCheckResponseMessage
            {
                AppStoredFileId = req.AppStoredFileId,
                Error = null,
                Message = "Processing check executed successfully."
            };

            var json = JsonSerializer.Serialize(response);
            await _httpClient.PostAsync($"{_apiBaseUrl}/status", new StringContent(json, Encoding.UTF8, "application/json"));
        }

        private async void SingleSync(int appStoredFileId, string path)
        {
            try
            {
                using var memoryStream = new MemoryStream();
                using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true);
                var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);

                foreach (var file in files)
                    archive.CreateEntryFromFile(file, Path.GetRelativePath(path, file));

                memoryStream.Seek(0, SeekOrigin.Begin);

                var payload = new AppFileUpdateResponseMessag
                {
                    AppStoredFileId = appStoredFileId,
                    MemoryStream = memoryStream.ToArray(),
                    UncompressedSize = GetDirectorySize(files)
                };

                var json = JsonSerializer.Serialize(payload);
                await _httpClient.PostAsync($"{_apiBaseUrl}/upload-file", new StringContent(json, Encoding.UTF8, "application/json"));
            }
            catch (Exception ex)
            {
                var error = new AppFileErrorMessage
                {
                    AppStoredFileId = appStoredFileId,
                    Mensagem = "An error occurred during the process of sync app file.",
                    Error = ex.Message
                };

                var json = JsonSerializer.Serialize(error);
                await _httpClient.PostAsync($"{_apiBaseUrl}/upload-error", new StringContent(json, Encoding.UTF8, "application/json"));
            }
        }

        private async void RequestSync(int appFileId)
        {
            var payload = new AppFileSyncRequestMessage { AppFileId = appFileId };
            var json = JsonSerializer.Serialize(payload);
            await _httpClient.PostAsync($"{_apiBaseUrl}/request-sync", new StringContent(json, Encoding.UTF8, "application/json"));
        }

        public async void ValidateSync(AppFileValidateStatusRequest req)
        {
            if (req?.AppFile == null)
                throw new ArgumentNullException(nameof(req.AppFile));

            var path = req.AppFile.Path;
            double sizeInBytes = 0;

            try
            {
                if (File.Exists(path))
                    sizeInBytes = new FileInfo(path).Length;
                else if (Directory.Exists(path))
                    sizeInBytes = GetDirectorySize(Directory.GetFiles(path, "*", SearchOption.AllDirectories));
                else
                    throw new FileNotFoundException($"Caminho não encontrado: {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao calcular tamanho: {ex.Message}");
            }

            var response = new AppFileValidateStatusResponse
            {
                AppFileId = req.AppFile.Id,
                SizeInBytes = sizeInBytes
            };

            var json = JsonSerializer.Serialize(response);
            await _httpClient.PostAsync($"{_apiBaseUrl}/validate", new StringContent(json, Encoding.UTF8, "application/json"));
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
    }
}
