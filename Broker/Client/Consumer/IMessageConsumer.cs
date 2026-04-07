using Broker.Contracts;


namespace Client.Consumer;

public interface IMessageConsumer
{
    IAsyncEnumerable<MessageEvent> SubscribeAsync(string queue, string consumerGroup, string consumerId);
    Task<bool> AckAsync(string messageId, string consumerId);
}