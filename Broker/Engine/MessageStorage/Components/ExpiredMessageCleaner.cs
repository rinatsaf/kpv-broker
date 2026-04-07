using System.Collections.Concurrent;
using System.Text.Json;

namespace Engine.MessageStorage.Components;

internal class ExpiredMessageCleaner(string rootPath, JsonSerializerOptions jsonOptions, ConcurrentDictionary<string, SemaphoreSlim> queueLocks)
    : BaseComponent(rootPath, jsonOptions, queueLocks)
{
    public async Task<int> ExpireMessagesAsync(CancellationToken ct = default)
    {
        var queuesPath = Path.Combine(_rootPath, "queues");
        if (!Directory.Exists(queuesPath))
            return 0;

        int totalExpired = 0;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var queueDir in Directory.GetDirectories(queuesPath))
        {
            var queueName = Path.GetFileName(queueDir);
            var messagesFile = Path.Combine(queueDir!, "messages.jsonl");

            if (!File.Exists(messagesFile)) continue;

            var semaphore = GetQueueSemaphore(queueName!);
            await semaphore.WaitAsync(ct);

            try
            {
                var lines = await File.ReadAllLinesAsync(messagesFile, ct);
                var originalCount = lines.Count(l => !string.IsNullOrWhiteSpace(l));

                var activeLines = new List<string>();

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var stored = JsonSerializer.Deserialize<StoredMessage>(line, _jsonOptions);
                    if (stored == null) continue;

                    // Удаляем сообщения с истёкшим TTL
                    if (stored.ExpiresAt.HasValue && stored.ExpiresAt.Value <= now)
                    {
                        totalExpired++;
                        await UpdateQueueMetadataAsync(queueName!, m => m.ExpiredTotal++, ct);
                        continue;
                    }

                    // Возвращаем "зависшие" InFlight-сообщения в Pending
                    if (stored.State == MessageState.InFlight && stored.VisibleUntil <= now)
                    {
                        stored.State = MessageState.Pending;
                        stored.VisibleUntil = now;
                        stored.ConsumerGroup = null;
                        stored.ConsumerId = null;
                    }

                    activeLines.Add(JsonSerializer.Serialize(stored, _jsonOptions));
                }

                if (activeLines.Count < originalCount)
                {
                    await SafeFileWriter.WriteLinesAsync(messagesFile, activeLines, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { /* продолжаем обработку других очередей */ }
            finally { semaphore.Release(); }
        }

        return totalExpired;
    }
}