using System.Reflection;
using Broker.Contracts;
using Core.Abstractions;

namespace Core.Services;

public sealed class MonitoringService(IMessageStorage messageStorage) : IMonitoringService
{
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private readonly string _version = ResolveVersion();

    public async Task<GetMetricsResponse> GetMetricsAsync(GetMetricsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.QueueName))
        {
            return await messageStorage.GetMetricsAsync(ct);
        }

        var stats = await messageStorage.GetStatsAsync(request.QueueName, ct);
        var metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            [$"queue.{request.QueueName}.published"] = stats.PublishedTotal,
            [$"queue.{request.QueueName}.consumed"] = stats.ConsumedTotal,
            [$"queue.{request.QueueName}.acknowledged"] = stats.AcknowledgedTotal,
            [$"queue.{request.QueueName}.rejected"] = stats.RejectedTotal,
            [$"queue.{request.QueueName}.expired"] = stats.ExpiredTotal,
            [$"queue.{request.QueueName}.avg_processing_ms"] = stats.AvgProcessingTimeMs,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        return new GetMetricsResponse
        {
            Metrics = { metrics }
        };
    }

    public async Task<BrokerStatus> GetBrokerStatusAsync(GetBrokerStatusRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var queues = await messageStorage.ListQueuesAsync(new ListQueuesRequest(), ct);
            long totalMessages = 0;

            foreach (var queue in queues.Queues)
            {
                totalMessages += queue.MessageCount;
            }

            return new BrokerStatus
            {
                IsHealthy = true,
                UptimeSeconds = GetUptimeSeconds(),
                Version = _version,
                ActiveConnections = 0,
                TotalQueues = queues.Queues.Count,
                TotalMessages = totalMessages
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new BrokerStatus
            {
                IsHealthy = false,
                UptimeSeconds = GetUptimeSeconds(),
                Version = _version,
                ActiveConnections = 0,
                TotalQueues = 0,
                TotalMessages = 0
            };
        }
    }

    private long GetUptimeSeconds()
    {
        var uptime = DateTimeOffset.UtcNow - _startedAtUtc;
        return uptime <= TimeSpan.Zero ? 0 : (long)uptime.TotalSeconds;
    }

    private static string ResolveVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version
            ?? typeof(MonitoringService).Assembly.GetName().Version;

        return version?.ToString() ?? "unknown";
    }
}
