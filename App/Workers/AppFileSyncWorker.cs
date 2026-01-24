using Application.Constants;
using Application.Services.AppFileWatcherService;
using Domain.Queues.AppFileDtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Web.Api.Toolkit.Queues.Application.Services;

namespace App.Workers
{
    public class AppFileSyncWorker : BackgroundService
    {
        private readonly IQueueService<AppFileProcessingQueueItem> _queue;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AppFileSyncWorker> _logger;

        public AppFileSyncWorker(
            IQueueService<AppFileProcessingQueueItem> queue,
            IServiceProvider serviceProvider,
            ILogger<AppFileSyncWorker> logger
        )
        {
            _queue = queue;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
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
                    MessageBox.Show($"Error: {ex.Message}", ApplicationConstants.ApplicationName);
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AppFileSyncWorker está parando. Liberando recursos...");

            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var appFileService = scope.ServiceProvider.GetRequiredService<IAppFileService>();
                    
                    appFileService.Dispose();
                    
                    _logger.LogInformation("FileSystemWatchers liberados com sucesso");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao liberar recursos do AppFileSyncWorker");
            }

            await base.StopAsync(cancellationToken);
        }
    }
}

