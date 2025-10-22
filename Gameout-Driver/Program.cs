using Application.Workers;
using Drivers.Services.AppFileWatcherService;
using Packages.Ws.Application.Workers;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSingleton<AppFileWatcherService>();
builder.Services.AddSingleton<WebSocketClientWorker>();
builder.Services.AddHostedService<AppFileDriverWorker>();

var app = builder.Build();

app.Run();