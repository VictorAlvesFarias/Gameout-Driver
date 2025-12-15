using Application.Services.AppFileWatcherService;
using Application.Services.LoggingService;
using Application.Workers;
using Domain.Queues.AppFileDtos;
using Infrastructure.Context;
using Web.Api.Toolkit.Queues.Application.Services;
using Web.Api.Toolkit.Ws.Application.Contexts;
using Web.Api.Toolkit.Ws.Application.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Registrar filas - IQueueService é registrado automaticamente pelo pacote
// mas precisamos garantir que está disponível
builder.Services.AddSingleton(typeof(IQueueService<>), typeof(QueueService<>));

// Registrar WebSocketRequestContextAccessor como Scoped
builder.Services.AddScoped<IWebSocketRequestContextAccessor, WebSocketRequestContextAccessor>();

builder.Services.AddSingleton<DriveWebSocketClientWorker>();
builder.Services.AddHostedService(e =>
{
    return e.GetRequiredService<DriveWebSocketClientWorker>();
});
builder.Services.AddSingleton<ApplicationContext>();
builder.Services.AddScoped<AppFileWatcherService>();
builder.Services.AddSingleton<ILoggingService, LoggingService>();

// Registrar todos os WebSocket channels para o DriveWebSocketClientWorker
// Cada worker pode ter seus próprios channels usando AddWebSocketChannels<TWorker>()
builder.Services.AddWebSocketChannels<DriveWebSocketClientWorker>();
builder.Services.AddHostedService<AppFileSyncWorker>();

var app = builder.Build();


app.UseHttpsRedirection();


app.Run();
