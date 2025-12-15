using Application.Services.AppFileWatcherService;
using Domain.Queues.AppFileDtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Web.Api.Toolkit.Queues.Application.Services;

namespace Application.Workers
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
            await foreach (var handle in _queue.DequeueWithHandleEnumerable(stoppingToken))
            {
                using var scope = _serviceProvider.CreateScope();
                var watcherService = scope.ServiceProvider.GetRequiredService<AppFileWatcherService>();

                await using (handle)
                {
                    await Task.Run(() => watcherService.ProcessSingleSync(handle.Item), stoppingToken);
                }
            }
        }
    }
}

