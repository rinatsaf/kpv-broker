using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Broker.Contracts;
using Core.Abstractions;
using Engine.MessageStorage.Components;

namespace Engine.MessageStorage;

/// <summary>
/// Реализация IMessageStorage с хранением в локальных файлах (JSONL).
/// Полностью соответствует protobuf-контракту broker.proto.
/// </summary>
public sealed class FileMessageStorage : IMessageStorage
{
    private readonly string _rootPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _queueLocks = new();
    private bool _disposed;

    private readonly MessageWriter messageWriter;
    private readonly MessageFetcher messageFetcher;
    private readonly MessageAckRejectMarker messageAckRejectMarker;
    private readonly QueueManager queueManager;
    private readonly DeadLetterQueueManager dlqManager;

    public FileMessageStorage(string rootPath, JsonSerializerOptions? jsonOptions = null)
    {
        _rootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_rootPath);

        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };

        messageWriter = new MessageWriter(_rootPath, _jsonOptions, _queueLocks);
        messageFetcher = new MessageFetcher(_rootPath, _jsonOptions, _queueLocks);
        messageAckRejectMarker = new MessageAckRejectMarker(_rootPath, _jsonOptions, _queueLocks);
        queueManager = new QueueManager(_rootPath, _jsonOptions, _queueLocks);
        dlqManager = new DeadLetterQueueManager(_rootPath, _jsonOptions, _queueLocks, messageWriter);
    }

    // ==================== ЗАПИСЬ ====================

    public Task<bool> TryStoreAsync(
        Message message,
        string queueName,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var targetQueue = string.IsNullOrWhiteSpace(queueName) ? message.Queue : queueName;
        if (string.IsNullOrWhiteSpace(targetQueue))
            return Task.FromResult(false);

        return messageWriter.StoreSingle(message, targetQueue, ct);
    }

    public Task<bool> TryStoreBatchAsync(
        IEnumerable<Message> messages,
        string queueName,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var messagesList = messages.ToList();
        if (messagesList.Count == 0) 
            return Task.FromResult(true);

        var targetQueue = string.IsNullOrWhiteSpace(queueName)
            ? messagesList.FirstOrDefault()?.Queue
            : queueName;

        if (string.IsNullOrWhiteSpace(targetQueue))
            return Task.FromResult(false);

        return messageWriter.StoreMultiple(messagesList, targetQueue, ct);
    }

    // ==================== ЧТЕНИЕ ДЛЯ КОНСЬЮМЕРОВ ====================

    public Task<IReadOnlyList<Message>> FetchAsync(
        string queueName,
        string consumerGroup,
        string consumerId,
        int maxCount,
        TimeSpan visibilityTimeout,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return messageFetcher.FetchAsync(queueName, consumerGroup, consumerId, maxCount, visibilityTimeout, ct);
    }

    public async Task<Message?> FetchOneAsync(
        string queueName,
        string consumerGroup,
        string consumerId,
        TimeSpan visibilityTimeout,
        CancellationToken ct = default)
    {
        var messages = await FetchAsync(queueName, consumerGroup, consumerId, 1, visibilityTimeout, ct);
        if (messages.Count == 0)
            return default;
        return messages[0];
    }

    // ==================== ПОДТВЕРЖДЕНИЕ / ОТКЛОНЕНИЕ ====================

    public Task<bool> TryAcknowledgeAsync(AcknowledgeRequest request, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return messageAckRejectMarker.TryAcknowledgeAsync(request, ct);
    }

    public Task<bool> TryRejectAsync(RejectRequest request, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return messageAckRejectMarker.TryRejectAsync(request, ct);
    }

    // ==================== УПРАВЛЕНИЕ ОЧЕРЕДЯМИ ====================

    public Task<bool> CreateQueueAsync(CreateQueueRequest request, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return queueManager.CreateQueueAsync(request, ct);
    }

    public Task<bool> DeleteQueueAsync(DeleteQueueRequest request, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return queueManager.DeleteQueueAsync(request.Name, ct);
    }

    public Task<QueueInfo> GetQueueInfoAsync(GetQueueInfoRequest request, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return queueManager.GetQueueInfoAsync(request.Name, ct);
    }

    public Task<ListQueuesResponse> ListQueuesAsync(ListQueuesRequest request, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return queueManager.ListQueuesAsync(ct);
    }

    public Task<bool> PurgeQueueAsync(PurgeQueueRequest request, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return queueManager.PurgeQueueAsync(request.Name, ct);        
    }

    // ==================== DEAD LETTER QUEUE ====================

    public Task<bool> MoveToDeadLetterAsync(Message message, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return dlqManager.MoveToDeadLetterAsync(message, ct);
    }

    public Task<IReadOnlyList<Message>> FetchFromDeadLetterAsync(
        string queueName,
        int maxCount,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return dlqManager.FetchFromDeadLetterAsync(queueName, maxCount, ct);
    }

    // ==================== МЕТРИКИ И МОНИТОРИНГ ====================

    public Task<QueueStats> GetStatsAsync(string queueName, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return queueManager.GetStatsAsync(queueName, ct);
    }

    public async Task<GetMetricsResponse> GetMetricsAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var queuesPath = Path.Combine(_rootPath, "queues");
        if (!Directory.Exists(queuesPath))
            return new GetMetricsResponse();

        var metrics = new Dictionary<string, double>();
        long totalMessages = 0, totalQueues = 0;

        foreach (var queueDir in Directory.GetDirectories(queuesPath))
        {
            var queueName = Path.GetFileName(queueDir);
            var stats = await GetStatsAsync(queueName!, ct);

            totalQueues++;
            totalMessages += stats.PublishedTotal;

            metrics[$"queue.{queueName}.published"] = stats.PublishedTotal;
            metrics[$"queue.{queueName}.consumed"] = stats.ConsumedTotal;
            metrics[$"queue.{queueName}.acknowledged"] = stats.AcknowledgedTotal;
            metrics[$"queue.{queueName}.avg_processing_ms"] = stats.AvgProcessingTimeMs;
        }

        metrics["total.queues"] = totalQueues;
        metrics["total.messages.published"] = totalMessages;
        metrics["storage.path_length"] = _rootPath.Length;
        metrics["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return new GetMetricsResponse { Metrics = { metrics } };
    }

    // ==================== ОБСЛУЖИВАНИЕ ====================

    public async Task<int> ExpireMessagesAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

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

            var @lock = GetQueueLock(queueName!);
            await @lock.WaitAsync(ct);

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
                        UpdateQueueStats(queueName!, m => m.ExpiredTotal++);
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
                    await WriteLinesAtomicAsync(messagesFile, activeLines, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { /* продолжаем обработку других очередей */ }
            finally { @lock.Release(); }
        }

        return totalExpired;
    }

    // ==================== IAsyncDisposable ====================

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        foreach (var @lock in _queueLocks.Values)
        {
            @lock.Dispose();
        }
        _queueLocks.Clear();
        _disposed = true;
        await ValueTask.CompletedTask;
    }

    // ==================== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ====================

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private string GetQueuePath(string queueName) =>
        Path.Combine(_rootPath, "queues", queueName);

    private SemaphoreSlim GetQueueLock(string queueName) =>
        _queueLocks.GetOrAdd(queueName, _ => new SemaphoreSlim(1, 1));

    private void UpdateQueueStats(string queueName, Action<QueueMetadata> update)
    {
        var metadataFile = Path.Combine(GetQueuePath(queueName), "metadata.json");
        if (!File.Exists(metadataFile)) return;

        try
        {
            var @lock = GetQueueLock(queueName);
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
        catch { /* игнорируем ошибки статистики */ }
    }

    private static async Task WriteLinesAtomicAsync(string targetFile, IEnumerable<string> lines, CancellationToken ct)
    {
        if (!lines.Any())
        {
            if (File.Exists(targetFile)) File.Delete(targetFile);
            return;
        }

        var tempFile = targetFile + ".tmp";
        await File.WriteAllLinesAsync(tempFile, lines, Encoding.UTF8, ct);
        File.Move(tempFile, targetFile, overwrite: true);
    }
}