using Application.Services.AppFileWatcherService;
using Application.Workers;
using Domain.Queues.AppFileDtos;
using Infrastructure.Context;
using Web.Api.Toolkit.Queues.Application.Services;

var builder = WebApplication.CreateBuilder(args);

// Registrar filas - IQueueService é registrado automaticamente pelo pacote
// mas precisamos garantir que está disponível
builder.Services.AddSingleton(typeof(IQueueService<>), typeof(QueueService<>));

builder.Services.AddSingleton<DriveWebSocketClientWorker>();
builder.Services.AddHostedService(e =>
{
    return e.GetRequiredService<DriveWebSocketClientWorker>();
});
builder.Services.AddSingleton<ApplicationContext>();
builder.Services.AddSingleton<AppFileWatcherService>();
builder.Services.AddHostedService<AppFileDriverWorker>();
builder.Services.AddHostedService<AppFileSyncWorker>();

var app = builder.Build();


app.UseHttpsRedirection();


app.Run();
