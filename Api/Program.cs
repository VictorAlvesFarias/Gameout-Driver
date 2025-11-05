using Application.Services.AppFileWatcherService;
using Application.Workers;
using Infrastructure.Context;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<DriveWebSocketClientWorker>();
builder.Services.AddHostedService(e =>
{
    return e.GetRequiredService<DriveWebSocketClientWorker>();
});
builder.Services.AddSingleton<ApplicationContext>();
builder.Services.AddSingleton<AppFileWatcherService>();
builder.Services.AddHostedService<AppFileDriverWorker>();

var app = builder.Build();


app.UseHttpsRedirection();


app.Run();
