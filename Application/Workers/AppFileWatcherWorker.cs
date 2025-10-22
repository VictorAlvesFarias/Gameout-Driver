using Domain.Entitites.ApplicationContext;
using Domain.Queues.AppFileDtos;
using Drivers.Services.AppFileWatcherService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Packages.Ws.Application.Workers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            var webSocketClientWorker = _serviceProvider.GetRequiredService<WebSocketClientWorker>();
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

            webSocketClientWorker.Subscribe("IsProcessing", async (message, cancellationToken) =>
            {
                var req = message.Deserialize<AppFileStatusCheckRequestMessage>();
                appFileWatcherService.IsProcessing(req);
            });

            webSocketClientWorker.Subscribe("ValidateSync", async (message, cancellationToken) =>
            {
                var req = message.Deserialize<AppFileValidateStatusRequest>();
                appFileWatcherService.ValidateSync(req);
            });

            return Task.CompletedTask;
        }
    }
}
