using Broker.Contracts;

namespace Core.Abstractions;

/// <summary>
/// Компонент постоянного хранения сообщений.
/// Гарантирует: сообщение сохранено на диск → можно доставлять консьюмерам.
/// </summary>
public interface IMessageStorage : IAsyncDisposable
{
    // ==================== ЗАПИСЬ ====================

    /// <summary>
    /// Сохраняет сообщение в хранилище.
    /// Возвращает успех только после фиксации на диске.
    /// </summary>
    Task<bool> TryStoreAsync(
        Message message,
        string queueName,
        CancellationToken ct = default);

    /// <summary>
    /// Пакетное сохранение сообщений (оптимизировано для диска).
    /// </summary>
    Task<bool> TryStoreBatchAsync(
        IEnumerable<Message> messages,
        string queueName,
        CancellationToken ct = default);

    // ==================== ЧТЕНИЕ ДЛЯ КОНСЬЮМЕРОВ ====================

    /// <summary>
    /// Получает сообщения для доставки консьюмеру.
    /// Сообщения помечаются как "в обработке" (не удаляются).
    /// </summary>
    Task<IReadOnlyList<Message>> FetchAsync(
        string queueName,
        string consumerGroup,
        string consumerId,
        int maxCount,
        TimeSpan visibilityTimeout,
        CancellationToken ct = default);

    /// <summary>
    /// Получает одно сообщение для синхронного Consume-режима.
    /// </summary>
    Task<Message?> FetchOneAsync(
        string queueName,
        string consumerGroup,
        string consumerId,
        TimeSpan visibilityTimeout,
        CancellationToken ct = default);

    // ==================== ПОДТВЕРЖДЕНИЕ / ОТКЛОНЕНИЕ ====================

    /// <summary>
    /// Подтверждает успешную обработку сообщения.
    /// Сообщение удаляется из очереди.
    /// </summary>
    Task<bool> TryAcknowledgeAsync(
        AcknowledgeRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Отклоняет сообщение с обработкой ошибки.
    /// </summary>
    Task<bool> TryRejectAsync(
        RejectRequest request,
        CancellationToken ct = default);

    // ==================== УПРАВЛЕНИЕ ОЧЕРЕДЯМИ ====================

    /// <summary>
    /// Создаёт новую очередь с настройками.
    /// </summary>
    Task<bool> CreateQueueAsync(
        CreateQueueRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Удаляет очередь и все её сообщения.
    /// </summary>
    Task<bool> DeleteQueueAsync(
        DeleteQueueRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Возвращает информацию о очереди.
    /// </summary>
    Task<QueueInfo> GetQueueInfoAsync(
        GetQueueInfoRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Список всех очередей (пагинация).
    /// </summary>
    Task<ListQueuesResponse> ListQueuesAsync(
        ListQueuesRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Очищает очередь (удаляет все сообщения, но не саму очередь).
    /// </summary>
    Task<bool> PurgeQueueAsync(
        PurgeQueueRequest request,
        CancellationToken ct = default);

    // ==================== DEAD LETTER QUEUE ====================

    /// <summary>
    /// Перемещает сообщение в DLQ после исчерпания попыток.
    /// </summary>
    Task<bool> MoveToDeadLetterAsync(
        Message message,
        CancellationToken ct = default);

    /// <summary>
    /// Получает сообщения из DLQ для анализа/повторной обработки.
    /// </summary>
    Task<IReadOnlyList<Message>> FetchFromDeadLetterAsync(
        string queueName,
        int maxCount,
        CancellationToken ct = default);

    // ==================== МЕТРИКИ И МОНИТОРИНГ ====================

    /// <summary>
    /// Возвращает статистику по очереди.
    /// </summary>
    Task<QueueStats> GetStatsAsync(
        string queueName,
        CancellationToken ct = default);

    /// <summary>
    /// Возвращает глобальные метрики хранилища.
    /// </summary>
    Task<GetMetricsResponse> GetMetricsAsync(
        CancellationToken ct = default);

    // ==================== ОБСЛУЖИВАНИЕ ====================

    /// <summary>
    /// Удаляет просроченные сообщения (TTL).
    /// Запускать периодически (background job).
    /// </summary>
    Task<int> ExpireMessagesAsync(
        CancellationToken ct = default);
}