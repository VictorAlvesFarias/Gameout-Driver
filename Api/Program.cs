using Application.Workers;
using Drivers.Services.AppFileWatcherService;
using Infrastructure.Context;
using Packages.Ws.Application.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(e =>
{
    var logger = e.GetRequiredService<ILoggerFactory>().CreateLogger<WebSocketClientWorker>();

    return new WebSocketClientWorker(logger, "ws://localhost:8051/ws");
});

builder.Services.AddHostedService(e =>
{
    return e.GetRequiredService<WebSocketClientWorker>();
});

builder.Services.AddSingleton<ApplicationContext>();
builder.Services.AddSingleton<AppFileWatcherService>();
builder.Services.AddHostedService<AppFileDriverWorker>();

var app = builder.Build();


app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
