using Domain.Entitites.ApplicationContext;
using Domain.Queues.AppFileDtos;
using Infrastructure.Context;
using System.IO.Compression;

namespace Drivers.Services.AppFileWatcherService
{
    public class AppFileWatcherService : IAppFileWatcherService
    {
        public readonly ApplicationContext _applicationContext;

        public AppFileWatcherService(
            ApplicationContext applicationContext,
            IQueueService<AppFileUpdateResponseMessag> fileSyncQueue,
            IQueueService<AppFileSyncRequestMessage> fileRequestSyncQueue,
            IQueueService<AppFileErrorMessage> fileErrorQueue,
            IQueueService<AppFileStatusCheckRequestMessage> statusCheckQueue,
            IQueueService<AppFileStatusCheckResponseMessage> responseQueue,
            IQueueService<AppFileUpdateRequestMessage> updateQueue,
            IQueueService<AppFileValidateStatusResponse> validateStatusQueue
        )
        {
            _applicationContext = applicationContext;
            _fileSyncQueue = fileSyncQueue;
            _fileRequestSyncQueue = fileRequestSyncQueue;
            _fileErrorQueue = fileErrorQueue;
            _updateQueue = updateQueue;
            _validateStatusQueue = validateStatusQueue;
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
                {
                    continue;
                }

                watcher = new AppFileWatcher()
                {
                    AppFileId = appFile.Id
                };

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
                    {
                        RequestSync(appFile.Id);
                    }

                    if (appFile.AutoValidateSync)
                    {
                        ValidateSync(new AppFileValidateStatusRequest() { AppFile = appFile });
                    }

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

        public void IsProcessing(AppFileStatusCheckRequestMessage req)
        {
            try
            {
                var isProcessing = _updateQueue.Contains(e => e.AppStoredFileId == req.AppStoredFileId);

                var response = new AppFileStatusCheckResponseMessage()
                {
                    AppStoredFileId = req.AppStoredFileId,
                    Error = isProcessing ? null : "It was not possible to find file in proccess queue.", // Se não está processando, pode ter erro
                    Message = isProcessing ? null : "File is no longer being processed."
                };

                _responseQueue.EnqueueAsync(response);
            }
            catch (Exception ex)
            {
                var errorResponse = new AppFileStatusCheckResponseMessage()
                {
                    AppStoredFileId = req.AppStoredFileId,
                    Error = ex.Message,
                    Message = $"It was not possible validate the processing."
                };

                _responseQueue.EnqueueAsync(errorResponse);
            }
        }

        private void SingleSync(int appStoredFileId, string path)
        {
            try
            {
                var memoryStream = new MemoryStream();
                using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true);
                var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    archive.CreateEntryFromFile(file, Path.GetRelativePath(path, file));
                }

                memoryStream.Seek(0, SeekOrigin.Begin);

                _fileSyncQueue.EnqueueAsync(new AppFileUpdateResponseMessag()
                {
                    AppStoredFileId = appStoredFileId,
                    MemoryStream = memoryStream.ToArray(),
                    UncompressedSize = GetDirectorySize(files)
                });
            }
            catch (Exception ex)
            {
                // Enviar erro para a fila de erros
                _fileErrorQueue.EnqueueAsync(new AppFileErrorMessage()
                {
                    AppStoredFileId = appStoredFileId,
                    Mensagem = "An error occurred during the process of sync app file.",
                    Error = ex.Message
                });
            }
        }

        private void RequestSync(int appFileId)
        {
            _fileRequestSyncQueue.EnqueueAsync(new AppFileSyncRequestMessage()
            {
                AppFileId = appFileId
            });
        }

        public void ValidateSync(AppFileValidateStatusRequest req)
        {
            if (req?.AppFile == null)
            {
                throw new ArgumentNullException(nameof(req.AppFile));
            }

            var path = req.AppFile.Path;
            double sizeInBytes = 0;

            try
            {
                if (File.Exists(path))
                {
                    // É um arquivo
                    var fileInfo = new FileInfo(path);
                    sizeInBytes = fileInfo.Length;
                }
                else if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                    sizeInBytes = GetDirectorySize(files);
                }
                else
                {
                    throw new FileNotFoundException($"Caminho não encontrado: {path}");
                }
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

            _validateStatusQueue.EnqueueAsync(response);
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
                catch
                {
                    // Ignora arquivos que não podem ser acessados
                }
            }

            return size;
        }
    }
}
