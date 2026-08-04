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

        // GPU support toggle (config [Settings] GpuAcceleration, default true). WPF
        // normally composites on the GPU; forcing SoftwareOnly makes the whole process
        // present in software — a safety valve for cabinets whose GPU driver glitches
        // or stutters. Process-wide, so it must be set before the first window renders.
        // (This governs WPF presentation only; the Skia lighting raster stays on CPU.)
        if (!config.GpuAcceleration)
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        }

        // The CPU lighting raster can saturate the machine and starve ES itself
        // (its script queue spawns one process per selection): events.ini then
        // lags minutes behind the frontend. Below-normal priority makes the
        // marquee the first thing to slow down, never the frontend.
        // belownormal (default) keeps the marquee below ES; normal lets it compete;
        // abovenormal/high let it win CPU against other Normal-class apps (useful on a
        // shared dev box, but on a real cabinet it can starve ES/the emulator). This is
        // the NAVIGATION priority; it is lowered to ProcessPriorityInGame during play.
        ProcessPriorityHelper.Apply(config.GetValue("Settings", "ProcessPriority", "belownormal"));

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
        // Lot 0 A/B (docs\Update.txt §4): record the effective priority so the two
        // runs (ProcessPriority=belownormal vs normal) can be told apart in the log.
        logger.LogInformation("[Lot0] effective process priority: {Priority} (config ProcessPriority={Config})",
            Process.GetCurrentProcess().PriorityClass, config.GetValue("Settings", "ProcessPriority", "belownormal"));
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
