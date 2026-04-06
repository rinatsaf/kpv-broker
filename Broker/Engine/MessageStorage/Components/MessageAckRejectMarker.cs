using System.Collections.Concurrent;
using System.Text.Json;
using Broker.Contracts;

namespace Engine.MessageStorage.Components;

internal class MessageAckRejectMarker(string rootPath, JsonSerializerOptions jsonOptions, ConcurrentDictionary<string, SemaphoreSlim> queueLocks)
    : BaseComponent(rootPath, jsonOptions, queueLocks)
{
    public async Task<bool> TryAcknowledgeAsync(
        AcknowledgeRequest request,
        CancellationToken ct = default)
    {
        // Находим очередь по ID сообщения (сканируем все очереди или используем индекс)
        var queueName = await FindQueueByMessageIdAsync(request.MessageId, ct);
        if (string.IsNullOrEmpty(queueName))
            return false;

        var messagesFile = GetQueueComponentPath(queueName, "messages.jsonl");
        var semaphore = GetQueueSemaphore(queueName);

        await semaphore.WaitAsync(ct);
        try
        {
            if (!File.Exists(messagesFile)) return false;

            var lines = await File.ReadAllLinesAsync(messagesFile, ct);
            var found = false;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            StoredMessage? stored = null;

            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                stored = JsonSerializer.Deserialize<StoredMessage>(lines[i], _jsonOptions);
                if (stored?.Id != request.MessageId) continue;

                // Можно подтвердить только InFlight-сообщения этого консьюмера
                if (stored.State != MessageState.InFlight ||
                    stored.ConsumerId != request.ConsumerId)
                    return false;

                stored.State = MessageState.Acknowledged;
                stored.AcknowledgedAt = now;
                stored.ProcessingTimeMs = stored.LastDeliveredAt.HasValue
                    ? (now - stored.LastDeliveredAt.Value) * 1000
                    : 0;

                lines[i] = JsonSerializer.Serialize(stored, _jsonOptions);
                found = true;
                break;
            }

            if (found)
            {
                // Физическое удаление подтверждённых сообщений
                var activeLines = lines.Where(l =>
                {
                    if (string.IsNullOrWhiteSpace(l)) return false;
                    var s = JsonSerializer.Deserialize<StoredMessage>(l, _jsonOptions);
                    return s?.State != MessageState.Acknowledged;
                }).ToList();

                await SafeFileWriter.WriteLinesAsync(messagesFile, activeLines, ct);
                await UpdateQueueMetadataAsync(queueName, m =>
                {
                    m.AcknowledgedTotal++;
                    UpdateAvgProcessingTime(m, stored!.ProcessingTimeMs);
                }, ct);
            }

            return found;
        }
        finally { semaphore.Release(); }
    }

    private static void UpdateAvgProcessingTime(QueueMetadata meta, double newTimeMs)
    {
        // Скользящее среднее: new_avg = old_avg * 0.9 + new_value * 0.1
        meta.AvgProcessingTimeMs = meta.AvgProcessingTimeMs * 0.9 + newTimeMs * 0.1;
    }

    public async Task<bool> TryRejectAsync(
        RejectRequest request,
        CancellationToken ct = default)
    {
        var queueName = await FindQueueByMessageIdAsync(request.MessageId, ct);
        if (string.IsNullOrEmpty(queueName))
            return false;

        var queuePath = GetQueuePath(queueName);
        var messagesFile = Path.Combine(queuePath, "messages.jsonl");
        var @lock = GetQueueSemaphore(queueName);
        var queueMeta = await LoadQueueMetadataAsync(queueName, ct);

        await @lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(messagesFile)) return false;

            var lines = await File.ReadAllLinesAsync(messagesFile, ct);
            var modified = false;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var movedToDlq = false;

            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                var stored = JsonSerializer.Deserialize<StoredMessage>(lines[i], _jsonOptions);
                if (stored?.Id != request.MessageId) continue;

                if (stored.State != MessageState.InFlight ||
                    stored.ConsumerId != request.ConsumerId)
                    return false;

                var maxAttempts = queueMeta?.MaxDeliveryAttempts ?? 3;

                if (request.Requeue && stored.DeliveryCount < maxAttempts)
                {
                    // Возврат в очередь с видимостью после небольшой задержки
                    stored.State = MessageState.Pending;
                    stored.VisibleUntil = now + 5; // 5 секунд backoff
                    stored.ConsumerGroup = null;
                    stored.ConsumerId = null;
                }
                else if (queueMeta?.DeadLetterEnabled == true && !string.IsNullOrEmpty(queueMeta.DeadLetterQueue))
                {
                    // Перемещение в DLQ
                    stored.State = MessageState.DeadLetter;
                    stored.DeadLetterReason = string.IsNullOrWhiteSpace(request.Reason)
                        ? "Max retries exceeded"
                        : request.Reason;
                    stored.DeadLetterAt = now;
                    movedToDlq = true;
                }
                else
                {
                    // Просто помечаем как отклонённое (будет удалено при очистке)
                    stored.State = MessageState.Rejected;
                }

                stored.RejectedAt = now;
                lines[i] = JsonSerializer.Serialize(stored, _jsonOptions);
                modified = true;
                break;
            }

            if (modified)
            {
                await SafeFileWriter.WriteLinesAsync(messagesFile, lines, ct);
                await UpdateQueueMetadataAsync(queueName, m => m.RejectedTotal++, ct);
                if (movedToDlq)
                    await UpdateQueueMetadataAsync(queueMeta!.DeadLetterQueue!, meta => meta.PublishedTotal++, ct);
            }

            return true;
        }
        finally { @lock.Release(); }
    }


    /// <summary>
    /// Находит очередь, содержащую сообщение с указанным ID.
    /// Для продакшена рекомендуется добавить индекс message_id → queue_name.
    /// </summary>
    private async Task<string?> FindQueueByMessageIdAsync(string messageId, CancellationToken ct)
    {
        var queuesPath = Path.Combine(_rootPath, "queues");
        if (!Directory.Exists(queuesPath)) return null;

        foreach (var queueDir in Directory.GetDirectories(queuesPath))
        {
            var queueName = Path.GetFileName(queueDir);
            var messagesFile = Path.Combine(queueDir, "messages.jsonl");

            if (!File.Exists(messagesFile)) continue;

            var semaphore = GetQueueSemaphore(queueName);
            await semaphore.WaitAsync(ct);
            try
            {
                var lines = await File.ReadAllLinesAsync(messagesFile, ct);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var stored = JsonSerializer.Deserialize<StoredMessage>(line, _jsonOptions);
                    if (stored?.Id == messageId)
                        return queueName;
                }
            }
            finally { semaphore.Release(); }
        }
        return null;
    }
}