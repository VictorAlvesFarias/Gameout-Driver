using Application.Services.AppFileWatcherService;
using Domain.Queues.AppFileDtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Application.Workers
{
    public class AppFileDriverWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;

        public AppFileDriverWorker(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var webSocketClientWorker = _serviceProvider.GetRequiredService<DriveWebSocketClientWorker>();
            var appFileWatcherService = _serviceProvider.GetRequiredService<AppFileWatcherService>();

            webSocketClientWorker.Subscribe("SetEvents", async (message, cancellationToken) =>
            {
                appFileWatcherService.SetWatchers();
            });

            webSocketClientWorker.Subscribe("SingleSync", async (message, cancellationToken) =>
            {
                var req = message.Deserialize<AppFileUpdateRequestMessage>();
                appFileWatcherService.SingleSync(req);
            });

            webSocketClientWorker.Subscribe("CheckStatus", async (message, cancellationToken) =>
            {
                var req = message.Deserialize<AppFileStatusCheckRequestMessage>();
                await appFileWatcherService.IsProcessing(req);
            });

            return Task.CompletedTask;
        }
    }
}
