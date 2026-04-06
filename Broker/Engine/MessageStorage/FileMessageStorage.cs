using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Broker.Contracts;
using Core.Abstractions;

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
    }

    // ==================== ЗАПИСЬ ====================

    public async Task<bool> TryStoreAsync(
        Message message,
        string queueName,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var targetQueue = string.IsNullOrWhiteSpace(queueName) ? message.Queue : queueName;
        if (string.IsNullOrWhiteSpace(targetQueue))
            return false;

        var queuePath = GetQueuePath(targetQueue);
        var messagesFile = Path.Combine(queuePath, "messages.jsonl");
        var @lock = GetQueueLock(targetQueue);

        await @lock.WaitAsync(ct);
        try
        {
            EnsureQueueDirectory(queuePath);

            // Загружаем метаданные очереди для применения настроек TTL
            var queueMeta = LoadQueueMetadata(targetQueue);

            var stored = MessageConverter.ToStored(message, queueMeta);
            var line = JsonSerializer.Serialize(stored, _jsonOptions) + "\n";

            await AppendLinesAtomicAsync(messagesFile, [line], ct);
            UpdateQueueStats(targetQueue, m => m.PublishedTotal++);

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }
        finally { @lock.Release(); }
    }

    public async Task<bool> TryStoreBatchAsync(
        IEnumerable<Message> messages,
        string queueName,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var messagesList = messages.ToList();
        if (messagesList.Count == 0) return true;

        var targetQueue = string.IsNullOrWhiteSpace(queueName)
            ? messagesList.FirstOrDefault()?.Queue
            : queueName;

        if (string.IsNullOrWhiteSpace(targetQueue))
            return false;

        var queuePath = GetQueuePath(targetQueue);
        var messagesFile = Path.Combine(queuePath, "messages.jsonl");
        var @lock = GetQueueLock(targetQueue);

        await @lock.WaitAsync(ct);
        try
        {
            EnsureQueueDirectory(queuePath);
            var queueMeta = LoadQueueMetadata(targetQueue);

            var lines = new List<string>(messagesList.Count);
            foreach (var msg in messagesList)
            {
                var stored = MessageConverter.ToStored(msg, queueMeta);
                lines.Add(JsonSerializer.Serialize(stored, _jsonOptions) + "\n");
            }

            await AppendLinesAtomicAsync(messagesFile, lines, ct);
            UpdateQueueStats(targetQueue, m => m.PublishedTotal += messagesList.Count);

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }
        finally { @lock.Release(); }
    }

    // ==================== ЧТЕНИЕ ДЛЯ КОНСЬЮМЕРОВ ====================

    public async Task<IReadOnlyList<Message>> FetchAsync(
        string queueName,
        string consumerGroup,
        string consumerId,
        int maxCount,
        TimeSpan visibilityTimeout,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var queuePath = GetQueuePath(queueName);
        var messagesFile = Path.Combine(queuePath, "messages.jsonl");
        var @lock = GetQueueLock(queueName);

        await @lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(messagesFile))
                return [];

            var results = new List<Message>(maxCount);
            var lines = await File.ReadAllLinesAsync(messagesFile, ct);
            var modified = false;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var visibleUntil = now + (long)visibilityTimeout.TotalSeconds;

            for (int i = 0; i < lines.Length && results.Count < maxCount; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                var stored = JsonSerializer.Deserialize<StoredMessage>(lines[i], _jsonOptions);
                if (stored == null) continue;

                // Пропускаем: невидимые, истёкшие, не в состоянии Pending
                if (stored.VisibleUntil > now ||
                    (stored.ExpiresAt.HasValue && stored.ExpiresAt.Value <= now) ||
                    stored.State != MessageState.Pending)
                    continue;

                // Помечаем как "в обработке"
                stored.State = MessageState.InFlight;
                stored.VisibleUntil = visibleUntil;
                stored.DeliveryCount++;
                stored.ConsumerGroup = consumerGroup;
                stored.ConsumerId = consumerId;
                stored.LastDeliveredAt = now;

                lines[i] = JsonSerializer.Serialize(stored, _jsonOptions);
                modified = true;

                results.Add(MessageConverter.ToProto(stored));
            }

            if (modified)
            {
                await WriteLinesAtomicAsync(messagesFile, lines, ct);
            }

            UpdateQueueStats(queueName, m => m.ConsumedTotal += results.Count);
            return results;
        }
        finally { @lock.Release(); }
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

    public async Task<bool> TryAcknowledgeAsync(
        AcknowledgeRequest request,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        // Находим очередь по ID сообщения (сканируем все очереди или используем индекс)
        var queueName = await FindMessageQueueAsync(request.MessageId, ct);
        if (string.IsNullOrEmpty(queueName))
            return false;

        var queuePath = GetQueuePath(queueName);
        var messagesFile = Path.Combine(queuePath, "messages.jsonl");
        var @lock = GetQueueLock(queueName);

        await @lock.WaitAsync(ct);
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

                await WriteLinesAtomicAsync(messagesFile, activeLines, ct);
                UpdateQueueStats(queueName, m =>
                {
                    m.AcknowledgedTotal++;
                    UpdateAvgProcessingTime(m, stored!.ProcessingTimeMs);
                });
            }

            return found;
        }
        finally { @lock.Release(); }
    }

    public async Task<bool> TryRejectAsync(
        RejectRequest request,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var queueName = await FindMessageQueueAsync(request.MessageId, ct);
        if (string.IsNullOrEmpty(queueName))
            return false;

        var queuePath = GetQueuePath(queueName);
        var messagesFile = Path.Combine(queuePath, "messages.jsonl");
        var @lock = GetQueueLock(queueName);
        var queueMeta = LoadQueueMetadata(queueName);

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
                await WriteLinesAtomicAsync(messagesFile, lines, ct);
                UpdateQueueStats(queueName, m =>
                {
                    m.RejectedTotal++;
                    if (movedToDlq) UpdateQueueStats(queueMeta!.DeadLetterQueue!, meta => meta.PublishedTotal++);
                });
            }

            return true;
        }
        finally { @lock.Release(); }
    }

    // ==================== УПРАВЛЕНИЕ ОЧЕРЕДЯМИ ====================

    public Task<bool> CreateQueueAsync(
        CreateQueueRequest request,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var queuePath = GetQueuePath(request.Name);

        try
        {
            Directory.CreateDirectory(queuePath);
            Directory.CreateDirectory(Path.Combine(queuePath, "indexes"));

            var metadata = new QueueMetadata
            {
                Name = request.Name,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                MaxSize = request.MaxSize,
                MessageTtlSeconds = request.MessageTtlSeconds,
                MaxDeliveryAttempts = request.MaxDeliveryAttempts > 0 ? request.MaxDeliveryAttempts : 3,
                DeadLetterEnabled = request.DeadLetterEnabled,
                DeadLetterQueue = request.DeadLetterEnabled ? request.DeadLetterQueue : null
            };

            var metadataFile = Path.Combine(queuePath, "metadata.json");
            var json = JsonSerializer.Serialize(metadata, _jsonOptions);
            File.WriteAllText(metadataFile, json, Encoding.UTF8);

            return Task.FromResult(true);
        }
        catch (Exception) { return Task.FromResult(false); }
    }

    public Task<bool> DeleteQueueAsync(
        DeleteQueueRequest request,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var queuePath = GetQueuePath(request.Name);
        var @lock = GetQueueLock(request.Name);

        return Task.Run(() =>
        {
            try
            {
                @lock.Wait();
                try
                {
                    if (Directory.Exists(queuePath))
                    {
                        Directory.Delete(queuePath, recursive: true);
                    }
                    _queueLocks.TryRemove(request.Name, out _);
                    return true;
                }
                finally { @lock.Release(); }
            }
            catch (Exception) { return false; }
        }, ct);
    }

    public async Task<QueueInfo> GetQueueInfoAsync(
        GetQueueInfoRequest request,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var queuePath = GetQueuePath(request.Name);
        var metadataFile = Path.Combine(queuePath, "metadata.json");

        if (!File.Exists(metadataFile))
            throw new InvalidOperationException($"Queue '{request.Name}' not found");

        var metadata = JsonSerializer.Deserialize<QueueMetadata>(
            await File.ReadAllTextAsync(metadataFile, ct), _jsonOptions);

        var stats = await GetStatsAsync(request.Name, ct);

        return new QueueInfo
        {
            Name = request.Name,
            MessageCount = stats.PublishedTotal,
            IsDeadLetterQueue = metadata?.DeadLetterEnabled == true &&
                               metadata?.DeadLetterQueue == request.Name
        };
    }

    public async Task<ListQueuesResponse> ListQueuesAsync(
        ListQueuesRequest request,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var queuesPath = Path.Combine(_rootPath, "queues");
        if (!Directory.Exists(queuesPath))
            return new ListQueuesResponse();

        var allQueues = Directory.GetDirectories(queuesPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .OrderBy(n => n);

        var queues = new List<QueueInfo>();
        foreach (var name in allQueues)
        {
            var stats = await GetStatsAsync(name!, ct);
            var meta = LoadQueueMetadata(name!);

            queues.Add(new QueueInfo
            {
                Name = name!,
                MessageCount = stats.PublishedTotal,
                IsDeadLetterQueue = meta?.DeadLetterEnabled == true &&
                                   meta?.DeadLetterQueue == name
            });
        }

        return new ListQueuesResponse
        {
            Queues = { queues }
        };
    }

    public async Task<bool> PurgeQueueAsync(
        PurgeQueueRequest request,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var queuePath = GetQueuePath(request.Name);
        var messagesFile = Path.Combine(queuePath, "messages.jsonl");
        var @lock = GetQueueLock(request.Name);

        await @lock.WaitAsync(ct);
        try
        {
            if (File.Exists(messagesFile))
            {
                var count = (await File.ReadAllLinesAsync(messagesFile, ct))
                    .Count(l => !string.IsNullOrWhiteSpace(l));

                File.Delete(messagesFile);
                return true;
            }
            return true;
        }
        catch (Exception) { return false; }
        finally { @lock.Release(); }
    }

    // ==================== DEAD LETTER QUEUE ====================

    public async Task<bool> MoveToDeadLetterAsync(
        Message message,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var queueName = message.Queue;
        if (string.IsNullOrEmpty(queueName)) return false;

        var queuePath = GetQueuePath(queueName);
        var messagesFile = Path.Combine(queuePath, "messages.jsonl");
        var @lock = GetQueueLock(queueName);
        var queueMeta = LoadQueueMetadata(queueName);

        await @lock.WaitAsync(ct);
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
                if (stored?.Id != message.Id) continue;

                stored.State = MessageState.DeadLetter;
                stored.DeadLetterReason = message.Headers.TryGetValue("LastError", out var err) ? err : "Manual move to DLQ";
                stored.DeadLetterAt = now;

                lines[i] = JsonSerializer.Serialize(stored, _jsonOptions);
                modified = true;
                break;
            }

            if (modified)
            {
                await WriteLinesAtomicAsync(messagesFile, lines, ct);

                // Если указана отдельная DLQ-очередь, копируем туда сообщение
                if (!string.IsNullOrEmpty(queueMeta?.DeadLetterQueue) &&
                    queueMeta.DeadLetterQueue != queueName)
                {
                    await TryStoreAsync(message, queueMeta.DeadLetterQueue, ct);
                }
            }

            return true;
        }
        catch (Exception) { return false; }
        finally { @lock.Release(); }
    }

    public async Task<IReadOnlyList<Message>> FetchFromDeadLetterAsync(
        string queueName,
        int maxCount,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var queuePath = GetQueuePath(queueName);
        var messagesFile = Path.Combine(queuePath, "messages.jsonl");
        var @lock = GetQueueLock(queueName);

        await @lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(messagesFile))
                return Array.Empty<Message>();

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
        finally { @lock.Release(); }
    }

    // ==================== МЕТРИКИ И МОНИТОРИНГ ====================

    public async Task<QueueStats> GetStatsAsync(
        string queueName,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var queuePath = GetQueuePath(queueName);
        var messagesFile = Path.Combine(queuePath, "messages.jsonl");
        var metadata = LoadQueueMetadata(queueName);

        if (!File.Exists(messagesFile))
        {
            return new QueueStats
            {
                PublishedTotal = metadata?.PublishedTotal ?? 0,
                ConsumedTotal = metadata?.ConsumedTotal ?? 0,
                AcknowledgedTotal = metadata?.AcknowledgedTotal ?? 0,
                RejectedTotal = metadata?.RejectedTotal ?? 0,
                ExpiredTotal = metadata?.ExpiredTotal ?? 0,
                AvgProcessingTimeMs = metadata?.AvgProcessingTimeMs ?? 0
            };
        }

        var lines = await File.ReadAllLinesAsync(messagesFile, ct);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        long total = 0, visible = 0, inFlight = 0, deadLetter = 0, expired = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var stored = JsonSerializer.Deserialize<StoredMessage>(line, _jsonOptions);
            if (stored == null) continue;

            total++;

            if (stored.ExpiresAt.HasValue && stored.ExpiresAt.Value <= now)
            {
                expired++;
                continue;
            }

            switch (stored.State)
            {
                case MessageState.Pending when stored.VisibleUntil <= now:
                    visible++;
                    break;
                case MessageState.InFlight:
                    inFlight++;
                    break;
                case MessageState.DeadLetter:
                    deadLetter++;
                    break;
            }
        }

        return new QueueStats
        {
            PublishedTotal = metadata?.PublishedTotal ?? 0,
            ConsumedTotal = metadata?.ConsumedTotal ?? 0,
            AcknowledgedTotal = metadata?.AcknowledgedTotal ?? 0,
            RejectedTotal = metadata?.RejectedTotal ?? 0,
            ExpiredTotal = (metadata?.ExpiredTotal ?? 0) + expired,
            AvgProcessingTimeMs = metadata?.AvgProcessingTimeMs ?? 0,

            // Дополнительные поля для внутренней логики (можно добавить в proto при необходимости)
            // TotalMessages = total,
            // VisibleMessages = visible,
            // InFlightMessages = inFlight,
            // DeadLetterCount = deadLetter
        };
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

    private static void EnsureQueueDirectory(string queuePath)
    {
        Directory.CreateDirectory(queuePath);
        Directory.CreateDirectory(Path.Combine(queuePath, "indexes"));
    }

    private QueueMetadata? LoadQueueMetadata(string queueName)
    {
        var metadataFile = Path.Combine(GetQueuePath(queueName), "metadata.json");
        if (!File.Exists(metadataFile)) return null;

        try
        {
            var json = File.ReadAllText(metadataFile, Encoding.UTF8);
            return JsonSerializer.Deserialize<QueueMetadata>(json, _jsonOptions);
        }
        catch { return null; }
    }

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

    private static void UpdateAvgProcessingTime(QueueMetadata meta, double newTimeMs)
    {
        // Скользящее среднее: new_avg = old_avg * 0.9 + new_value * 0.1
        meta.AvgProcessingTimeMs = meta.AvgProcessingTimeMs * 0.9 + newTimeMs * 0.1;
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

    private static async Task AppendLinesAtomicAsync(string targetFile, IEnumerable<string> lines, CancellationToken ct)
    {
        if (!lines.Any()) return;

        var tempFile = targetFile + ".tmp";
        if (File.Exists(targetFile))
        {
            File.Copy(targetFile, tempFile);
        }
        await File.AppendAllLinesAsync(tempFile, lines, Encoding.UTF8, ct);
        File.Move(tempFile, targetFile, overwrite: true);
    }

    /// <summary>
    /// Находит очередь, содержащую сообщение с указанным ID.
    /// Для продакшена рекомендуется добавить индекс message_id → queue_name.
    /// </summary>
    private async Task<string?> FindMessageQueueAsync(string messageId, CancellationToken ct)
    {
        var queuesPath = Path.Combine(_rootPath, "queues");
        if (!Directory.Exists(queuesPath)) return null;

        foreach (var queueDir in Directory.GetDirectories(queuesPath))
        {
            var queueName = Path.GetFileName(queueDir);
            var messagesFile = Path.Combine(queueDir, "messages.jsonl");

            if (!File.Exists(messagesFile)) continue;

            var @lock = GetQueueLock(queueName!);
            await @lock.WaitAsync(ct);
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
            finally { @lock.Release(); }
        }
        return null;
    }
}