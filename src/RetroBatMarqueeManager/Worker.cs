using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Infrastructure.Processes;

namespace RetroBatMarqueeManager;

public sealed class Worker : BackgroundService
{
    private readonly MarqueeController _surfaces;
    private readonly IDmdService _dmd;
    private readonly ILogger<Worker> _logger;

    public Worker(MarqueeController surfaces, IDmdService dmd, ILogger<Worker> logger)
    {
        _surfaces = surfaces;
        _dmd = dmd;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _surfaces.StartAsync(stoppingToken);
        await _dmd.InitializeAsync(stoppingToken);
        _logger.LogInformation("MarqueeManager surfaces and private DMD stack are ready");
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        _dmd.Stop();
        await _surfaces.StopAsync();
    }
}
