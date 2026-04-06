using System.Collections.Concurrent;
using System.Text.Json;
using Broker.Contracts;

namespace Engine.MessageStorage.Components;

internal class MetricsCollector(string rootPath, JsonSerializerOptions jsonOptions, ConcurrentDictionary<string, SemaphoreSlim> queueLocks, QueueManager queueManager)
    : BaseComponent(rootPath, jsonOptions, queueLocks)
{
    private readonly QueueManager _queueManager = queueManager;
    
    public async Task<GetMetricsResponse> GetMetricsAsync(CancellationToken ct = default)
    {
        var queuesPath = Path.Combine(_rootPath, "queues");
        if (!Directory.Exists(queuesPath))
            return new GetMetricsResponse();

        var metrics = new Dictionary<string, double>();
        long totalMessages = 0, totalQueues = 0;

        foreach (var queueDir in Directory.GetDirectories(queuesPath))
        {
            var queueName = Path.GetFileName(queueDir);
            var stats = await _queueManager.GetStatsAsync(queueName!, ct);

            totalQueues++;
            totalMessages += stats.PublishedTotal;

            metrics[$"queue.{queueName}.published"] = stats.PublishedTotal;
            metrics[$"queue.{queueName}.consumed"] = stats.ConsumedTotal;
            metrics[$"queue.{queueName}.acknowledged"] = stats.AcknowledgedTotal;
            metrics[$"queue.{queueName}.avg_processing_ms"] = stats.AvgProcessingTimeMs;
        }

        metrics["total.queues"] = totalQueues;
        metrics["total.messages.published"] = totalMessages;
        metrics["storage.path_length"] = _rootPath.Length;
        metrics["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return new GetMetricsResponse { Metrics = { metrics } };
    }
}