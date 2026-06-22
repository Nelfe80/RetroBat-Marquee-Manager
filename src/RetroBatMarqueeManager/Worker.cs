using RetroBatMarqueeManager.Application.Workflows;
using RetroBatMarqueeManager.Core.Interfaces;

namespace RetroBatMarqueeManager
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfigService _config;
        private readonly MarqueeWorkflow _workflow;
        private readonly Infrastructure.Processes.MpvController _mpv;
        private readonly IMarqueeFileFinder _marqueeFinder;
        private readonly IProcessService _processService; // Need direct access for generic Kill
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly IInputService _inputService;

        public Worker(
            ILogger<Worker> logger,
            IConfigService config,
            MarqueeWorkflow workflow,
            Infrastructure.Processes.MpvController mpv,
            IMarqueeFileFinder marqueeFinder,
            IProcessService processService,
            IHostApplicationLifetime appLifetime,
            IInputService inputService)
        {
            _logger = logger;
            _config = config;
            _workflow = workflow;
            _mpv = mpv;
            _marqueeFinder = marqueeFinder;
            _processService = processService;
            _appLifetime = appLifetime;
            _inputService = inputService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RetroBat Marquee Manager Service starting...");

            // 1. Force Kill MPV/dmdext on Startup (Clean State)
            // EN: Kill orphaned processes from previous crashed/killed sessions
            // FR: Tuer les processus orphelins des sessions précédentes crashées/tuées
            _logger.LogInformation("Ensuring no previous MPV/dmdext instances are running...");
            _processService.KillProcess("mpv");
            _processService.KillProcess("dmdext");
            
            // 2. Register Shutdown Hook
            // EN: Cleanup processes on graceful shutdown
            // FR: Nettoyer les processus lors d'un arrêt gracieux
            _appLifetime.ApplicationStopping.Register(() =>
            {
                _logger.LogInformation("Application stopping, killing MPV and dmdext...");
                _processService.KillProcess("mpv");
                _processService.KillProcess("dmdext");
            });

            // Initialize MarqueeFileFinder with es_settings.cfg
            // We use ConfigureAwait(false) generally good practice
            var esSettingsPath = Path.Combine(_config.RetroBatPath, "emulationstation", ".emulationstation", "es_settings.cfg");
            await _marqueeFinder.InitializeAsync(esSettingsPath);
            
            // Start components
            _mpv.StartMpv();

            // Wait for WPF window to initialize, then show default image
            await Task.Delay(1500, stoppingToken);
            if (!string.IsNullOrEmpty(_config.DefaultImagePath))
            {
                await _mpv.DisplayImage(_config.DefaultImagePath, loop: true);
                _logger.LogInformation($"Displayed default marquee on startup: {_config.DefaultImagePath}");
            }

            _workflow.Start();

            _logger.LogInformation("RetroBat Marquee Manager Service running.");

            while (!stoppingToken.IsCancellationRequested)
            {
                _inputService.Update();
                await Task.Delay(20, stoppingToken); // 50fps polling
            }
            
            // Should be handled by ApplicationStopping above, but safe to have here too
             _logger.LogInformation("Service loop ended.");
        }
    }
}
