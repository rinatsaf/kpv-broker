using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Engine.MessageStorage.Components;

internal class BaseComponent(string rootPath, JsonSerializerOptions jsonOptions, ConcurrentDictionary<string, SemaphoreSlim> queueLocks)
{
    protected readonly string _rootPath = rootPath;
    protected readonly JsonSerializerOptions _jsonOptions = jsonOptions;
    protected readonly ConcurrentDictionary<string, SemaphoreSlim> _queueLocks = queueLocks;

    // =============== Queue path common operations ========== 

    protected string GetQueuePath(string queueName) =>
        Path.Combine(_rootPath, "queues", queueName);

    protected string GetQueueComponentPath(string queueName, string component) =>
        Path.Combine(_rootPath, "queues", queueName, component);

    protected SemaphoreSlim GetQueueSemaphore(string queueName) =>
        _queueLocks.GetOrAdd(queueName, _ => new SemaphoreSlim(1, 1));

    // ================== Metadata operations ==================

    protected async Task<QueueMetadata?> LoadQueueMetadataAsync(string queueName, CancellationToken ct)
    {
        var metadataFile = GetQueueComponentPath(queueName, "metadata.json");
        if (!File.Exists(metadataFile)) return null;

        try
        {
            // var json = await File.ReadAllTextAsync(metadataFile, Encoding.UTF8);
            // return JsonSerializer.Deserialize<QueueMetadata>(json, _jsonOptions);
            // TODO как-нибудь сравнить че лучше вообще варик выше или ниже
            using var stream = new FileStream(metadataFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<QueueMetadata>(stream, _jsonOptions, cancellationToken: ct);
        }
        catch { return null; }
    }

    protected async Task UpdateQueueMetadataAsync(string queueName, Action<QueueMetadata> update, CancellationToken ct)
    {
        var metadataFile = GetQueueComponentPath(queueName, "metadata.json");
        if (!File.Exists(metadataFile)) return;

        try
        {
            var semaphore = GetQueueSemaphore(queueName);
            await semaphore.WaitAsync(ct);
            try
            {
                QueueMetadata? meta;

                await using (var readStream = new FileStream(metadataFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous))
                {
                    meta = await JsonSerializer.DeserializeAsync<QueueMetadata>(readStream, _jsonOptions, ct);
                }

                if (meta != null)
                {
                    update(meta);

                    var tempFile = metadataFile + ".tmp";
                    await using (var tempStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                    {
                        await JsonSerializer.SerializeAsync(tempStream, meta, _jsonOptions, ct);
                    }

                    File.Move(tempFile, metadataFile, overwrite: true);
                }
            }
            finally { semaphore.Release(); }
        }
        catch { }
    }
}