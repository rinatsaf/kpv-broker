using Broker.Contracts;


namespace Client.Consumer;

public interface IMessageConsumer
{
    IAsyncEnumerable<MessageEvent> SubscribeAsync(string queue, string consumerGroup, string consumerId, CancellationToken ct);
    Task<IEnumerable<Message>> ConsumeBatchAsync(string queue, string consumerGroup, string consumerId, int maxMessages, CancellationToken ct);

    Task<bool> AckAsync(string messageId, string consumerId, bool success = true, CancellationToken ct = default);
    Task<RejectResult> RejectAsync(string messageId, string consumerId, CancellationToken ct = default);
}