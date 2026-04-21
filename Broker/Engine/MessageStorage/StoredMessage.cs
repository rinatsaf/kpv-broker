namespace Engine.MessageStorage;

/// <summary>
/// Внутреннее представление сообщения для хранения на диске.
/// Расширяет proto-сообщение метаданными состояния.
/// </summary>
internal sealed class StoredMessage
{
    // === Поля из proto Message ===
    public string Id { get; set; } = string.Empty;
    public string Queue { get; set; } = string.Empty;
    public byte[] Payload { get; set; } = [];
    public Dictionary<string, string> Headers { get; set; } = [];
    public long Timestamp { get; set; } // unix epoch seconds
    public int TtlSeconds { get; set; }
    public int DeliveryCount { get; set; }
    
    // === Метаданные хранилища ===
    public MessageState State { get; set; } = MessageState.Pending;
    public long VisibleUntil { get; set; } // unix epoch seconds
    public long? ExpiresAt { get; set; }   // unix epoch seconds (если задан TTL)
    
    public string? ConsumerGroup { get; set; }
    public string? ConsumerId { get; set; }
    public long? LastDeliveredAt { get; set; }
    
    public string? DeadLetterReason { get; set; }
    public long? DeadLetterAt { get; set; }
    
    // === Статистика обработки ===
    public long? AcknowledgedAt { get; set; }
    public long? RejectedAt { get; set; }
    public double ProcessingTimeMs { get; set; }
}

internal enum MessageState
{
    Pending = 0,        // Ожидает доставки
    InFlight = 1,       // Доставлен консьюмеру
    Acknowledged = 2,   // Подтверждён (помечен на удаление)
    Rejected = 3,       // Отклонён
    DeadLetter = 4      // В dead letter queue
}

internal sealed class QueueMetadata
{
    public string Name { get; set; } = string.Empty;
    public long CreatedAt { get; set; } // unix epoch
    
    public int MaxSize { get; set; } = -1; // -1 = без лимита
    public int MessageTtlSeconds { get; set; } = 0; // 0 = без TTL
    public int MaxDeliveryAttempts { get; set; } = 3;
    
    public bool DeadLetterEnabled { get; set; }
    public string? DeadLetterQueue { get; set; }
    
    // Статистика
    public long PublishedTotal { get; set; }
    public long ConsumedTotal { get; set; }
    public long AcknowledgedTotal { get; set; }
    public long RejectedTotal { get; set; }
    public long ExpiredTotal { get; set; }
    public double AvgProcessingTimeMs { get; set; }
}