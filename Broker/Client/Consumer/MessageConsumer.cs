using Broker.Contracts;
using Client.Connection;
using Grpc.Core;

namespace Client.Consumer;

public class MessageConsumer(IBrokerConnection connection) : IMessageConsumer
{
    public async IAsyncEnumerable<MessageEvent> SubscribeAsync(string queue, string consumerGroup, string consumerId)
    {
        var client = connection.GetConsumerClient();
        var request = new SubscribeRequest
        {
            Queue = queue,
            ConsumerGroup = consumerGroup,
            ConsumerId = consumerId
        };

        using var call = client.Subscribe(request);

        await foreach (var @event in call.ResponseStream.ReadAllAsync())
        {
            yield return @event;
        }
    }

    public async Task<bool> AckAsync(string messageId, string consumerId)
    {
        var client = connection.GetConsumerClient();
        var result = await client.AcknowledgeAsync(new AcknowledgeRequest 
        { 
            MessageId = messageId, 
            ConsumerId = consumerId, 
            Success = true 
        });
        return result.Acknowledged;
    }
}