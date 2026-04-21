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
            var expiredInQueue = 0;
            await semaphore.WaitAsync(ct);

            try
            {
                var lines = await File.ReadAllLinesAsync(messagesFile, ct);
                var originalCount = lines.Count(l => !string.IsNullOrWhiteSpace(l));
                var activeLines = new List<string>();
                var stateChanged = false;

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var stored = JsonSerializer.Deserialize<StoredMessage>(line, _jsonOptions);
                    if (stored == null) continue;

                    if (stored.ExpiresAt.HasValue && stored.ExpiresAt.Value <= now)
                    {
                        totalExpired++;
                        expiredInQueue++;
                        continue;
                    }

                    if (stored.State == MessageState.InFlight && stored.VisibleUntil <= now)
                    {
                        stored.State = MessageState.Pending;
                        stored.VisibleUntil = now;
                        stored.ConsumerGroup = null;
                        stored.ConsumerId = null;
                        stateChanged = true;
                    }

                    activeLines.Add(JsonSerializer.Serialize(stored, _jsonOptions));
                }

                if (activeLines.Count < originalCount || stateChanged)
                {
                    await SafeFileWriter.WriteLinesAsync(messagesFile, activeLines, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { }
            finally { semaphore.Release(); }

            if (expiredInQueue > 0)
                await UpdateQueueMetadataAsync(queueName!, m => m.ExpiredTotal += expiredInQueue, ct);
        }

        return totalExpired;
    }
}
