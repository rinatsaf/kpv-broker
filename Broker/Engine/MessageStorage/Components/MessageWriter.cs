using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Broker.Contracts;

namespace Engine.MessageStorage.Components;

internal class MessageWriter(string rootPath, JsonSerializerOptions jsonOptions, ConcurrentDictionary<string, SemaphoreSlim> queueLocks)
    : BaseComponent(rootPath, jsonOptions, queueLocks)
{
    public async Task<bool> StoreSingle(Message message, string targetQueue, CancellationToken ct)
    {
        var messagesFilePath = GetQueueComponentPath(targetQueue, "messages.jsonl");
        var semaphore = GetQueueSemaphore(targetQueue);

        await semaphore.WaitAsync(ct);
        try
        {
            var queueMeta = await LoadQueueMetadataAsync(targetQueue, ct);

            var stored = MessageConverter.ToStored(message, queueMeta);
            var line = JsonSerializer.Serialize(stored, _jsonOptions) + "\n";

            await AppendLinesAtomicAsync(messagesFilePath, [line], ct);
            await UpdateQueueMetadataAsync(targetQueue, m => m.PublishedTotal++, ct);

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }
        finally { semaphore.Release(); }
    }

    public async Task<bool> StoreMultiple(List<Message> messagesList, string targetQueue, CancellationToken ct)
    {
        var messagesFilePath = GetQueueComponentPath(targetQueue, "messages.jsonl");
        var semaphore = GetQueueSemaphore(targetQueue);

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

            await AppendLinesAtomicAsync(messagesFilePath, lines, ct);
            await UpdateQueueMetadataAsync(targetQueue, m => m.PublishedTotal += messagesList.Count, ct);

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }
        finally { semaphore.Release(); }
    }

    private static async Task AppendLinesAtomicAsync(string targetFile, IEnumerable<string> lines, CancellationToken ct)
    {
        if (!lines.Any()) return;
        Directory.CreateDirectory(Directory.GetParent(targetFile)!.FullName);

        var tempFile = targetFile + ".tmp";
        if (File.Exists(targetFile))
        {
            File.Copy(targetFile, tempFile);
        }
        await File.AppendAllLinesAsync(tempFile, lines, Encoding.UTF8, ct);
        File.Move(tempFile, targetFile, overwrite: true);
    }
}