using System.Collections.Concurrent;
using System.Text.Json;
using Broker.Contracts;

namespace Engine.MessageStorage.Components;

internal class MessageFetcher(string rootPath, JsonSerializerOptions jsonOptions, ConcurrentDictionary<string, SemaphoreSlim> queueLocks, ILogger<MessageFetcher> logger)
    : BaseComponent(rootPath, jsonOptions, queueLocks, logger)
{
    public async Task<IReadOnlyList<Message>> FetchAsync(
        string queueName,
        string consumerGroup,
        string consumerId,
        int maxCount,
        TimeSpan visibilityTimeout,
        CancellationToken ct = default)
    {
        var messagesFile = GetQueueComponentPath(queueName, "messages.jsonl");
        var semaphore = GetQueueSemaphore(queueName);
        var results = new List<Message>(maxCount);
        var consumedCount = 0;

        await semaphore.WaitAsync(ct);
        try
        {
            if (!File.Exists(messagesFile))
                return [];

            var lines = await File.ReadAllLinesAsync(messagesFile, ct);
            var modified = false;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var visibleUntil = now + (long)visibilityTimeout.TotalSeconds;

            for (int i = 0; i < lines.Length && results.Count < maxCount; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                var stored = JsonSerializer.Deserialize<StoredMessage>(lines[i], _jsonOptions);
                if (stored == null) continue;

                // Пропускаем: невидимые, истёкшие, не в состоянии Pending
                if (stored.VisibleUntil > now ||
                    (stored.ExpiresAt.HasValue && stored.ExpiresAt.Value <= now) ||
                    stored.State != MessageState.Pending)
                    continue;

                // Помечаем как "в обработке"
                stored.State = MessageState.InFlight;
                stored.VisibleUntil = visibleUntil;
                stored.DeliveryCount++;
                stored.ConsumerGroup = consumerGroup;
                stored.ConsumerId = consumerId;
                stored.LastDeliveredAt = now;

                lines[i] = JsonSerializer.Serialize(stored, _jsonOptions);
                modified = true;

                results.Add(MessageConverter.ToProto(stored));
            }

            if (modified)
            {
                await SafeFileWriter.WriteLinesAsync(messagesFile, lines, ct);
            }

            consumedCount = results.Count;
        }
        finally { semaphore.Release(); }

        if (consumedCount > 0)
            await UpdateQueueMetadataAsync(queueName, m => m.ConsumedTotal += consumedCount, ct);

        return results;
    }
}
