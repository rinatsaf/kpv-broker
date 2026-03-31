// Internal/MessageConverter.cs
using Broker.Contracts;

namespace Engine.MessageStorage;

internal static class MessageConverter
{
    public static StoredMessage ToStored(Message proto, QueueMetadata? queueMeta = null)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        return new StoredMessage
        {
            Id = proto.Id,
            Queue = proto.Queue,
            Payload = proto.Payload.ToByteArray(),
            Headers = proto.Headers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            Timestamp = proto.Timestamp > 0 ? proto.Timestamp : now,
            TtlSeconds = proto.TtlSeconds > 0 ? proto.TtlSeconds : (queueMeta?.MessageTtlSeconds ?? 0),
            DeliveryCount = proto.DeliveryCount,
            
            VisibleUntil = now,
            ExpiresAt = proto.TtlSeconds > 0 || (queueMeta?.MessageTtlSeconds ?? 0) > 0
                ? now + Math.Max(proto.TtlSeconds, queueMeta?.MessageTtlSeconds ?? 0)
                : null,
            State = MessageState.Pending
        };
    }

    public static Message ToProto(StoredMessage stored)
    {
        return new Message
        {
            Id = stored.Id,
            Queue = stored.Queue,
            Payload = Google.Protobuf.ByteString.CopyFrom(stored.Payload),
            Headers = { stored.Headers },
            Timestamp = stored.Timestamp,
            TtlSeconds = stored.TtlSeconds,
            DeliveryCount = stored.DeliveryCount
        };
    }
}