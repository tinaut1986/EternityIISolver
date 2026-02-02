using EternityServer.Data;
using EternityShared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace EternityServer.Services;

public class ZombieJobCleaner : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ZombieJobCleaner> _logger;

    public ZombieJobCleaner(IServiceProvider serviceProvider, ILogger<ZombieJobCleaner> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<EternityDbContext>();

                var cutoff = DateTime.UtcNow.AddMinutes(-1);
                var zombies = await context.Jobs
                    .Where(j => j.Status == JobStatus.Assigned && (j.LastHeartbeat == null || j.LastHeartbeat < cutoff))
                    .ToListAsync(stoppingToken);

                if (zombies.Any())
                {
                    _logger.LogInformation("Cleaning up {Count} zombie jobs.", zombies.Count);
                    foreach (var job in zombies)
                    {
                        job.Status = JobStatus.Pending;
                        job.AssignedWorkerId = null;
                    }
                    await context.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning zombie jobs.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
