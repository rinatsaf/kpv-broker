using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Broker.Contracts;

namespace Engine.MessageStorage.Components;

internal class QueueManager(string rootPath, JsonSerializerOptions jsonOptions, ConcurrentDictionary<string, SemaphoreSlim> queueLocks)
    : BaseComponent(rootPath, jsonOptions, queueLocks)
{
    public async Task<bool> CreateQueueAsync(CreateQueueRequest request, CancellationToken ct = default)
    {
        var queuePath = GetQueuePath(request.Name);

        try
        {
            Directory.CreateDirectory(queuePath);

            var metadata = new QueueMetadata
            {
                Name = request.Name,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                MaxSize = request.MaxSize,
                MessageTtlSeconds = request.MessageTtlSeconds,
                MaxDeliveryAttempts = request.MaxDeliveryAttempts > 0 ? request.MaxDeliveryAttempts : 3,
                DeadLetterEnabled = request.DeadLetterEnabled,
                DeadLetterQueue = request.DeadLetterEnabled ? request.DeadLetterQueue : null
            };

            var metadataFile = Path.Combine(queuePath, "metadata.json");
            var json = JsonSerializer.Serialize(metadata, _jsonOptions);
            await File.WriteAllTextAsync(metadataFile, json, Encoding.UTF8, ct);

            return true;
        }
        catch (Exception) { return false; }
    }

    public async Task<bool> DeleteQueueAsync(string name, CancellationToken ct = default)
    {
        var queuePath = GetQueuePath(name);
        var semaphore = GetQueueSemaphore(name);

        try
        {
            await semaphore.WaitAsync(ct);
            try
            {
                if (Directory.Exists(queuePath))
                {
                    Directory.Delete(queuePath, recursive: true);
                }
                _queueLocks.TryRemove(name, out _);
                return true;
            }
            finally { semaphore.Release(); }
        }
        catch (Exception) { return false; }
    }

    public async Task<QueueInfo> GetQueueInfoAsync(string name, CancellationToken ct = default)
    {
        var queuePath = GetQueuePath(name);
        var metadataFile = Path.Combine(queuePath, "metadata.json");

        if (!File.Exists(metadataFile))
            throw new InvalidOperationException($"Queue '{name}' not found");

        var metadata = JsonSerializer.Deserialize<QueueMetadata>(
            await File.ReadAllTextAsync(metadataFile, ct), _jsonOptions);

        var stats = await GetStatsAsync(name, ct);

        return new QueueInfo
        {
            Name = name,
            MessageCount = stats.PublishedTotal,
            IsDeadLetterQueue = metadata?.DeadLetterEnabled == true && metadata?.DeadLetterQueue == name
        };
    }

    public async Task<ListQueuesResponse> ListQueuesAsync(CancellationToken ct = default)
    {
        var queuesPath = Path.Combine(_rootPath, "queues");
        if (!Directory.Exists(queuesPath))
            return new ListQueuesResponse();

        var allQueues = Directory.GetDirectories(queuesPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .OrderBy(n => n);

        var queues = new List<QueueInfo>();
        foreach (var name in allQueues)
        {
            if (name == null) continue;
            var stats = await GetStatsAsync(name, ct);
            var meta = await LoadQueueMetadataAsync(name, ct);

            queues.Add(new QueueInfo
            {
                Name = name,
                MessageCount = stats.PublishedTotal,
                IsDeadLetterQueue = meta?.DeadLetterEnabled == true && meta?.DeadLetterQueue == name
            });
        }

        return new ListQueuesResponse
        {
            Queues = { queues }
        };
    }

    public async Task<bool> PurgeQueueAsync(string name, CancellationToken ct = default)
    {
        var queuePath = GetQueuePath(name);
        var messagesFile = Path.Combine(queuePath, "messages.jsonl");
        var semaphore = GetQueueSemaphore(name);

        await semaphore.WaitAsync(ct);
        try
        {
            if (File.Exists(messagesFile))
            {
                var count = (await File.ReadAllLinesAsync(messagesFile, ct))
                    .Count(l => !string.IsNullOrWhiteSpace(l));

                File.Delete(messagesFile);
                return true;
            }
            return true;
        }
        catch (Exception) { return false; }
        finally { semaphore.Release(); }
    }

    public async Task<QueueStats> GetStatsAsync(string queueName, CancellationToken ct = default)
    {
        var metadata = await LoadQueueMetadataAsync(queueName, ct);

        if (metadata != null)
        {
            return new QueueStats
            {
                PublishedTotal = metadata.PublishedTotal,
                ConsumedTotal = metadata.ConsumedTotal,
                AcknowledgedTotal = metadata.AcknowledgedTotal,
                RejectedTotal = metadata.RejectedTotal,
                ExpiredTotal = metadata.ExpiredTotal,
                AvgProcessingTimeMs = metadata.AvgProcessingTimeMs
            };
        }

        var messagesFile = GetQueueComponentPath(queueName, "messages.jsonl");
        if (!File.Exists(messagesFile))
        {
            return new QueueStats();
        }

        var lines = await File.ReadAllLinesAsync(messagesFile, ct);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        long total = 0, visible = 0, inFlight = 0, deadLetter = 0, expired = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            StoredMessage? stored;
            try
            {
                stored = JsonSerializer.Deserialize<StoredMessage>(line, _jsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (stored == null) continue;

            total++;

            if (stored.ExpiresAt.HasValue && stored.ExpiresAt.Value <= now)
            {
                expired++;
                continue;
            }

            switch (stored.State)
            {
                case MessageState.Pending when stored.VisibleUntil <= now:
                    visible++;
                    break;
                case MessageState.InFlight:
                    inFlight++;
                    break;
                case MessageState.DeadLetter:
                    deadLetter++;
                    break;
            }
        }

        return new QueueStats
        {
            PublishedTotal = metadata?.PublishedTotal ?? 0,
            ConsumedTotal = metadata?.ConsumedTotal ?? 0,
            AcknowledgedTotal = metadata?.AcknowledgedTotal ?? 0,
            RejectedTotal = metadata?.RejectedTotal ?? 0,
            ExpiredTotal = (metadata?.ExpiredTotal ?? 0) + expired,
            AvgProcessingTimeMs = metadata?.AvgProcessingTimeMs ?? 0,

            // Дополнительные поля для внутренней логики (можно добавить в proto при необходимости)
            // TotalMessages = total,
            // VisibleMessages = visible,
            // InFlightMessages = inFlight,
            // DeadLetterCount = deadLetter
        };
    }
}
