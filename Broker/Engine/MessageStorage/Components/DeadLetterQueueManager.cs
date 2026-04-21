using System.Collections.Concurrent;
using System.Text.Json;
using Broker.Contracts;

namespace Engine.MessageStorage.Components;

internal class DeadLetterQueueManager(string rootPath, JsonSerializerOptions jsonOptions, ConcurrentDictionary<string, SemaphoreSlim> queueLocks, MessageWriter messageWriter, ILogger<DeadLetterQueueManager> logger)
    : BaseComponent(rootPath, jsonOptions, queueLocks, logger)
{
    private MessageWriter _messageWriter = messageWriter;

    public async Task<bool> MoveToDeadLetterAsync(Message message, CancellationToken ct = default)
    {
        var queueName = message.Queue;
        if (string.IsNullOrEmpty(queueName)) return false;

        var messagesFile = GetQueueComponentPath(queueName, "messages.jsonl");
        var semaphore = GetQueueSemaphore(queueName);
        var queueMeta = await LoadQueueMetadataAsync(queueName, ct);

        await semaphore.WaitAsync(ct);
        try
        {
            if (!File.Exists(messagesFile) || !queueMeta?.DeadLetterEnabled == true)
                return false;

            var lines = await File.ReadAllLinesAsync(messagesFile, ct);
            var modified = false;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                var stored = JsonSerializer.Deserialize<StoredMessage>(lines[i], _jsonOptions);
                if (stored?.Id != message.Id)
                    continue;

                stored.State = MessageState.DeadLetter;
                stored.DeadLetterReason = message.Headers.TryGetValue("LastError", out var err) ? err : "Manual move to DLQ";
                stored.DeadLetterAt = now;

                lines[i] = JsonSerializer.Serialize(stored, _jsonOptions);
                modified = true;
                break;
            }

            if (modified)
            {
                await SafeFileWriter.WriteLinesAsync(messagesFile, lines, ct);

                // Если указана отдельная DLQ-очередь, копируем туда сообщение
                if (!string.IsNullOrEmpty(queueMeta?.DeadLetterQueue) &&
                    queueMeta.DeadLetterQueue != queueName)
                {
                    await _messageWriter.StoreSingle(message, queueMeta.DeadLetterQueue, ct);
                }
            }

            return true;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to move message to dead letter queue for queue {Queue}", queueName); return false; }
        finally { semaphore.Release(); }
    }

    public async Task<IReadOnlyList<Message>> FetchFromDeadLetterAsync(
        string queueName,
        int maxCount,
        CancellationToken ct = default)
    {

        var queuePath = GetQueuePath(queueName);
        var messagesFile = Path.Combine(queuePath, "messages.jsonl");
        var semaphore = GetQueueSemaphore(queueName);

        await semaphore.WaitAsync(ct);
        try
        {
            if (!File.Exists(messagesFile))
                return [];

            var results = new List<Message>();
            var lines = await File.ReadAllLinesAsync(messagesFile, ct);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var stored = JsonSerializer.Deserialize<StoredMessage>(line, _jsonOptions);
                if (stored?.State == MessageState.DeadLetter)
                {
                    results.Add(MessageConverter.ToProto(stored));
                    if (results.Count >= maxCount) break;
                }
            }

            return results;
        }
        finally
        {
            semaphore.Release();
        }
    }
}