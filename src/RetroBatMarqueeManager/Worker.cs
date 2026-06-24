using RetroBatMarqueeManager.Application.Workflows;
using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Infrastructure.Processes;

namespace RetroBatMarqueeManager
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfigService _config;
        private readonly MarqueeWorkflow _workflow;
        private readonly MarqueeController _marquee;
        private readonly IMarqueeFileFinder _marqueeFinder;
        private readonly IProcessService _processService;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly IInputService _inputService;

        public Worker(
            ILogger<Worker> logger,
            IConfigService config,
            MarqueeWorkflow workflow,
            MarqueeController marquee,
            IMarqueeFileFinder marqueeFinder,
            IProcessService processService,
            IHostApplicationLifetime appLifetime,
            IInputService inputService)
        {
            _logger = logger;
            _config = config;
            _workflow = workflow;
            _marquee = marquee;
            _marqueeFinder = marqueeFinder;
            _processService = processService;
            _appLifetime = appLifetime;
            _inputService = inputService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RetroBat Marquee Manager Service starting...");

            _processService.KillProcess("dmdext");

            _appLifetime.ApplicationStopping.Register(() =>
            {
                _logger.LogInformation("Application stopping, killing dmdext...");
                _processService.KillProcess("dmdext");
            });

            var esSettingsPath = Path.Combine(_config.RetroBatPath, "emulationstation", ".emulationstation", "es_settings.cfg");
            await _marqueeFinder.InitializeAsync(esSettingsPath);

            _marquee.StartMpv();

            await Task.Delay(1500, stoppingToken);
            if (!string.IsNullOrEmpty(_config.DefaultImagePath))
            {
                await _marquee.DisplayImage(_config.DefaultImagePath, loop: true);
                _logger.LogInformation($"Displayed default marquee on startup: {_config.DefaultImagePath}");
            }

            _workflow.Start();

            _logger.LogInformation("RetroBat Marquee Manager Service running.");

            while (!stoppingToken.IsCancellationRequested)
            {
                _inputService.Update();
                await Task.Delay(20, stoppingToken);
            }

            _logger.LogInformation("Service loop ended.");
        }
    }
}
