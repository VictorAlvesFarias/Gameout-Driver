using Application.Services.AppFileWatcherService;
using Domain.Queues.AppFileDtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Web.Api.Toolkit.Queues.Application.Services;

namespace App.Workers
{
    public class AppFileSyncWorker : BackgroundService
    {
        private readonly IQueueService<AppFileProcessingQueueItem> _queue;
        private readonly IServiceProvider _serviceProvider;

        public AppFileSyncWorker(
            IQueueService<AppFileProcessingQueueItem> queue,
            IServiceProvider serviceProvider
        )
        {
            _queue = queue;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Inicializar watchers ao iniciar a aplicação
            using (var scope = _serviceProvider.CreateScope())
            {
                var appFileService = scope.ServiceProvider.GetRequiredService<IAppFileService>();
                appFileService.SetWatchers();
            }

            await foreach (var handle in _queue.DequeueWithHandleEnumerable(stoppingToken))
            {
                using var scope = _serviceProvider.CreateScope();
                var workerService = scope.ServiceProvider.GetRequiredService<IAppFileWorkerService>();

                try
                {
                    await using (handle)
                    {
                        await Task.Run(() => workerService.ProcessSingleSync(handle.Item), stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}");
                }
            }
        }
    }
}

