using Application.Services.AppFileWatcherService;
using Application.Services.LoggingService;
using App.Workers;
using App.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using Web.Api.Toolkit.Queues.Application.Services;
using Web.Api.Toolkit.Ws.Application.Contexts;
using Web.Api.Toolkit.Ws.Application.Extensions;
using App.Services;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureAppConfiguration((context, config) =>
    {
        // Garantir que appsettings.json seja carregado do diretório correto
        var env = context.HostingEnvironment;
        config.SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        // Configurar timeout de shutdown para garantir que todos os serviços parem corretamente
        services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
        });

        // Registrar filas - IQueueService é registrado automaticamente pelo pacote
        // mas precisamos garantir que está disponível
        services.AddSingleton(typeof(IQueueService<>), typeof(QueueService<>));

        services.AddSingleton<DriveWebSocketClientWorker>();
        services.AddHostedService(e =>
        {
            return e.GetRequiredService<DriveWebSocketClientWorker>();
        });
        
        services.AddSingleton<Infrastructure.Context.ApplicationContext>();
        
        // Registrar os novos serviços separados
        services.AddScoped<IAppFileService, AppFileService>();
        services.AddScoped<IAppFileWorkerService, AppFileWorkerService>();
        services.AddScoped<IAppFileUtilsService, AppFileUtilsService>();
        services.AddSingleton<IUtilsService, UtilsService>();

        // Registrar todos os WebSocket channels para o DriveWebSocketClientWorker
        // IMPORTANTE: AddWebSocketChannels já registra o IWebSocketRequestContextAccessor
        var currentAssembly = Assembly.GetExecutingAssembly();
        services.AddWebSocketChannels<DriveWebSocketClientWorker>(ServiceLifetime.Scoped, currentAssembly);
        
        services.AddHostedService<AppFileSyncWorker>();

        // Register System Tray (includes auto-start functionality)
        services.AddHostedService<SystemTrayHostedService>();
    })
    .Build();

await host.RunAsync();
