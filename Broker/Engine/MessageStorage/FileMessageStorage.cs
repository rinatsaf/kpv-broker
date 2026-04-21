using System.Collections.Concurrent;
using System.Text.Json;
using Broker.Contracts;
using Core.Abstractions;
using Engine.MessageStorage.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Engine.MessageStorage;

/// <summary>
/// Реализация IMessageStorage с хранением в локальных файлах (JSONL).
/// По сути, многофункциональный фасад, который скрывает за собой кучу мелких сервисов
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
    private readonly MetricsCollector metricsCollector;
    private readonly ExpiredMessageCleaner expiredMessageCleaner;

    public FileMessageStorage(string rootPath, JsonSerializerOptions? jsonOptions = null)
        : this(Options.Create(new FileMessageStorageOptions { RootPath = rootPath, JsonOptions = jsonOptions ?? new JsonSerializerOptions() }), NullLoggerFactory.Instance)
    {
    }

    public FileMessageStorage(IOptions<FileMessageStorageOptions> options, ILoggerFactory loggerFactory)
    {
        var rootPath = Path.GetFullPath(options.Value.RootPath);
        Directory.CreateDirectory(rootPath);
        _rootPath = rootPath;
        _jsonOptions = options.Value.JsonOptions;

        messageWriter = new MessageWriter(_rootPath, _jsonOptions, _queueLocks, 
            loggerFactory.CreateLogger<MessageWriter>());
        messageFetcher = new MessageFetcher(_rootPath, _jsonOptions, _queueLocks, 
            loggerFactory.CreateLogger<MessageFetcher>());
        messageAckRejectMarker = new MessageAckRejectMarker(_rootPath, _jsonOptions, _queueLocks, 
            loggerFactory.CreateLogger<MessageAckRejectMarker>());
        queueManager = new QueueManager(_rootPath, _jsonOptions, _queueLocks, 
            loggerFactory.CreateLogger<QueueManager>());
        dlqManager = new DeadLetterQueueManager(_rootPath, _jsonOptions, _queueLocks, messageWriter, 
            loggerFactory.CreateLogger<DeadLetterQueueManager>());
        metricsCollector = new MetricsCollector(_rootPath, _jsonOptions, _queueLocks, queueManager, 
            loggerFactory.CreateLogger<MetricsCollector>());
        expiredMessageCleaner = new ExpiredMessageCleaner(_rootPath, _jsonOptions, _queueLocks, 
            loggerFactory.CreateLogger<ExpiredMessageCleaner>());
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

    public Task<GetMetricsResponse> GetMetricsAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return metricsCollector.GetMetricsAsync(ct);
    }

    // ==================== ОБСЛУЖИВАНИЕ ====================

    public Task<int> ExpireMessagesAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return expiredMessageCleaner.ExpireMessagesAsync(ct);
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
}