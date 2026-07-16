using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RetroBatMarqueeManager.Application.Services;
using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Infrastructure.Configuration;
using RetroBatMarqueeManager.Infrastructure.Logging;
using RetroBatMarqueeManager.Infrastructure.Native;
using RetroBatMarqueeManager.Infrastructure.Processes;
using RetroBatMarqueeManager.Infrastructure.UI;

namespace RetroBatMarqueeManager;

public static class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        // headless batch mode: pre-render every templated composition of the given
        // systems so ES navigation shows them instantly (Setup's "Pré-générer").
        // Deliberately OUTSIDE the single-instance mutex: it only writes the cache.
        var renderIndex = Array.IndexOf(args, "--render-templates");
        if (renderIndex >= 0)
        {
            Environment.Exit(Application.Media.TemplateBatchRenderer.Run(
                renderIndex + 1 < args.Length ? args[renderIndex + 1] : ""));
            return;
        }

        using var mutex = new Mutex(true, "RetroBatMarqueeManager.SingleInstance", out var ownsMutex);
        if (!ownsMutex) return;

        var config = new IniConfigService();
        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                if (config.LogToFile) logging.AddProvider(new SimpleFileLoggerProvider(config.LogFilePath));
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<IConfigService>(config);
                services.AddSingleton(config);
                services.AddSingleton<MarqueeController>();
                services.AddSingleton<DmdDeviceWrapper>();
                services.AddSingleton<DmdFrameRenderer>();
                services.AddSingleton<IDmdService, DmdService>();
                services.AddSingleton<LayManager>();
                services.AddSingleton<SurfacePresentationService>();
                services.AddSingleton<InstructionCardService>();
                services.AddSingleton<TrayIconService>();
                services.AddHostedService<Worker>();
                services.AddHostedService<RetroBatMonitorService>();
                services.AddHostedService<WebSocketListenerService>();
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        logger.LogInformation("MarqueeManager {Version} starting; APIExpose is the only media and data source", typeof(Program).Assembly.GetName().Version);
        await host.StartAsync();

        if (Process.GetProcessesByName("explorer").Length > 0 && config.MinimizeToTray)
        {
            var tray = host.Services.GetRequiredService<TrayIconService>();
            tray.Initialize(() => host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication());
            host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(tray.StopMessageLoop);
            tray.RunMessageLoop();
        }
        else
        {
            await host.WaitForShutdownAsync();
        }

        await host.StopAsync(TimeSpan.FromSeconds(5));
    }
}
