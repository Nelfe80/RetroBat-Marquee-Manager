using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RetroBatMarqueeManager.Application.Services
{
    public class RetroBatMonitorService : BackgroundService
    {
        private readonly ILogger<RetroBatMonitorService> _logger;
        private readonly IHostApplicationLifetime _appLifetime;
        private const string ProcessName = "emulationstation";
        private const int CheckIntervalMs = 30000; // Check every 30 seconds
        private const int GracePeriodMinutes = 5;

        private DateTime? _missingSince = null;

        public RetroBatMonitorService(ILogger<RetroBatMonitorService> logger, IHostApplicationLifetime appLifetime)
        {
            _logger = logger;
            _appLifetime = appLifetime;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RetroBat Monitor Service started. Monitoring 'emulationstation' process.");

            // Initial delay to let RetroBat start if launched together
            await Task.Delay(10000, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var processes = Process.GetProcessesByName(ProcessName);
                    bool isRunning = processes.Length > 0;

                    if (isRunning)
                    {
                        if (_missingSince != null)
                        {
                            _logger.LogInformation("RetroBat process detected. Resetting shutdown timer.");
                            _missingSince = null;
                        }
                    }
                    else
                    {
                        if (_missingSince == null)
                        {
                            _missingSince = DateTime.Now;
                            _logger.LogWarning($"RetroBat process not found. Grace period started ({GracePeriodMinutes} min).");
                        }
                        else
                        {
                            var timeMissing = DateTime.Now - _missingSince.Value;
                            if (timeMissing.TotalMinutes >= GracePeriodMinutes)
                            {
                                _logger.LogWarning($"RetroBat has been missing for {timeMissing.TotalMinutes:F1} minutes. Initiating application shutdown.");
                                _appLifetime.StopApplication();
                                return; // Exit loop
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error in RetroBat Monitor: {ex.Message}");
                }

                await Task.Delay(CheckIntervalMs, stoppingToken);
            }
        }
    }
}
