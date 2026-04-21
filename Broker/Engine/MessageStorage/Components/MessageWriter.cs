using System.Collections.Concurrent;
using System.Text.Json;
using Broker.Contracts;

namespace Engine.MessageStorage.Components;

internal class MessageWriter(string rootPath, JsonSerializerOptions jsonOptions, ConcurrentDictionary<string, SemaphoreSlim> queueLocks, ILogger<MessageWriter> logger)
    : BaseComponent(rootPath, jsonOptions, queueLocks, logger)
{
    public async Task<bool> StoreSingle(Message message, string targetQueue, CancellationToken ct)
    {
        var messagesFilePath = GetQueueComponentPath(targetQueue, "messages.jsonl");
        var semaphore = GetQueueSemaphore(targetQueue);
        var written = false;

        await semaphore.WaitAsync(ct);
        try
        {
            var queueMeta = await LoadQueueMetadataAsync(targetQueue, ct);

            var stored = MessageConverter.ToStored(message, queueMeta);
            var line = JsonSerializer.Serialize(stored, _jsonOptions) + "\n";

            await SafeFileWriter.AppendLinesAsync(messagesFilePath, [line], ct);
            written = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger.LogError(ex, "Failed to store message to queue {Queue}", targetQueue); return false; }
        finally { semaphore.Release(); }

        if (written)
            await UpdateQueueMetadataAsync(targetQueue, m => m.PublishedTotal++, ct);

        return true;
    }

    public async Task<bool> StoreMultiple(List<Message> messagesList, string targetQueue, CancellationToken ct)
    {
        var messagesFilePath = GetQueueComponentPath(targetQueue, "messages.jsonl");
        var semaphore = GetQueueSemaphore(targetQueue);
        var written = false;

        await semaphore.WaitAsync(ct);
        try
        {
            var queueMeta = await LoadQueueMetadataAsync(targetQueue, ct);

            var lines = new List<string>(messagesList.Count);
            foreach (var msg in messagesList)
            {
                var stored = MessageConverter.ToStored(msg, queueMeta);
                lines.Add(JsonSerializer.Serialize(stored, _jsonOptions) + "\n");
            }

            await SafeFileWriter.AppendLinesAsync(messagesFilePath, lines, ct);
            written = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger.LogError(ex, "Failed to store {Count} messages to queue {Queue}", messagesList.Count, targetQueue); return false; }
        finally { semaphore.Release(); }

        if (written)
            await UpdateQueueMetadataAsync(targetQueue, m => m.PublishedTotal += messagesList.Count, ct);

        return true;
    }
}
