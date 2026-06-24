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
        private const int CheckIntervalMs = 1000; // Check every 1 second
        private const int GracePeriodMs = 3500;   // Grace period of 3.5 seconds

        private DateTime? _missingSince = null;

        public RetroBatMonitorService(ILogger<RetroBatMonitorService> logger, IHostApplicationLifetime appLifetime)
        {
            _logger = logger;
            _appLifetime = appLifetime;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RetroBat Monitor Service started. Monitoring 'emulationstation' process.");

            // Wait until emulationstation is detected for the first time (armed state)
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var processes = Process.GetProcessesByName(ProcessName);
                    bool isRunning = processes.Length > 0;

                    if (isRunning)
                    {
                        _logger.LogInformation("RetroBat EmulationStation process detected. Lifecycle monitoring armed.");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error checking RetroBat process on startup: {ex.Message}");
                }

                await Task.Delay(CheckIntervalMs, stoppingToken);
            }

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
                            _logger.LogWarning($"RetroBat process not found. Grace period started ({GracePeriodMs} ms).");
                        }
                        else
                        {
                            var timeMissing = DateTime.Now - _missingSince.Value;
                            if (timeMissing.TotalMilliseconds >= GracePeriodMs)
                            {
                                _logger.LogWarning($"RetroBat has been missing for {timeMissing.TotalMilliseconds:F0} ms. Initiating application shutdown.");
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
