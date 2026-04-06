using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Engine.MessageStorage.Components;

internal class BaseComponent(string rootPath, JsonSerializerOptions jsonOptions, ConcurrentDictionary<string, SemaphoreSlim> queueLocks)
{
    protected readonly string _rootPath = rootPath;
    protected readonly JsonSerializerOptions _jsonOptions = jsonOptions;
    protected readonly ConcurrentDictionary<string, SemaphoreSlim> _queueLocks = queueLocks;

    protected string GetQueuePath(string queueName) =>
        Path.Combine(_rootPath, "queues", queueName);
    
    protected string GetQueueComponentPath(string queueName, string component) =>
        Path.Combine(_rootPath, "queues", queueName, component);

    protected SemaphoreSlim GetQueueSemaphore(string queueName) =>
        _queueLocks.GetOrAdd(queueName, _ => new SemaphoreSlim(1, 1));

    // 

    protected QueueMetadata? LoadQueueMetadata(string queueName)
    {
        var metadataFile = GetQueueComponentPath(queueName, "metadata.json");
        if (!File.Exists(metadataFile)) return null;

        try
        {
            var json = File.ReadAllText(metadataFile, Encoding.UTF8);
            return JsonSerializer.Deserialize<QueueMetadata>(json, _jsonOptions);
        }
        catch { return null; }
    }

    protected void UpdateQueueMetadata(string queueName, Action<QueueMetadata> update)
    {
        var metadataFile = GetQueueComponentPath(queueName, "metadata.json");
        if (!File.Exists(metadataFile)) return;

        try
        {
            var @lock = GetQueueSemaphore(queueName);
            @lock.Wait();
            try
            {
                var json = File.ReadAllText(metadataFile, Encoding.UTF8);
                var meta = JsonSerializer.Deserialize<QueueMetadata>(json, _jsonOptions);
                if (meta != null)
                {
                    update(meta);
                    json = JsonSerializer.Serialize(meta, _jsonOptions);
                    File.WriteAllText(metadataFile, json, Encoding.UTF8);
                }
            }
            finally { @lock.Release(); }
        }
        catch {}
    }
}