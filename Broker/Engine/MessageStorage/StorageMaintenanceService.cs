using Core.Abstractions;

namespace Engine.MessageStorage;

public class StorageMaintenanceService(IMessageStorage storage, ILogger<StorageMaintenanceService> logger) : BackgroundService
{
    private readonly IMessageStorage _storage = storage;
    private readonly ILogger<StorageMaintenanceService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var expired = await _storage.ExpireMessagesAsync(stoppingToken);
                if (expired > 0)
                    _logger.LogInformation("Expired {Count} messages", expired);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during message expiration");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}