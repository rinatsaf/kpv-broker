using System.Runtime.CompilerServices;
using Broker.Contracts;
using Client.Connection;
using Grpc.Core;

namespace Client.Consumer;

public class MessageConsumer(IBrokerConnection connection) : IMessageConsumer
{
    private readonly ConsumerService.ConsumerServiceClient _client = connection.GetConsumerClient();

    public async IAsyncEnumerable<MessageEvent> SubscribeAsync(
        string queue,
        string consumerGroup,
        string consumerId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var request = new SubscribeRequest
        {
            Queue = queue,
            ConsumerGroup = consumerGroup,
            ConsumerId = consumerId
        };

        var call = _client.Subscribe(request, cancellationToken: ct);

        await foreach (var @event in call.ResponseStream.ReadAllAsync(cancellationToken: ct))
        {
            yield return @event;
        }
    }

    public async Task<IEnumerable<Message>> ConsumeBatchAsync(
        string queue,
        string consumerGroup,
        string consumerId,
        int maxMessages,
        CancellationToken ct)
    {
        var result = await _client.ConsumeAsync(new ConsumeRequest
        {
            Queue = queue,
            ConsumerGroup = consumerGroup,
            ConsumerId = consumerId,
            MaxMessages = maxMessages
        }, cancellationToken: ct);

        return result.Messages;
    }

    public async Task<bool> AckAsync(string messageId, string consumerId, bool success = true, CancellationToken ct = default)
    {
        var result = await _client.AcknowledgeAsync(new AcknowledgeRequest
        {
            MessageId = messageId,
            ConsumerId = consumerId,
            Success = success
        }, cancellationToken: ct);
        return result.Acknowledged;
    }

    public async Task<RejectResult> RejectAsync(string messageId, string consumerId, CancellationToken ct = default)
    {
        var result = await _client.RejectAsync(new RejectRequest
        {
            MessageId = messageId,
            ConsumerId = consumerId
        }, cancellationToken: ct);

        return new RejectResult
        {
            Success = result.Rejected,
            MovedToDlq = result.MovedToDlq
        };
    }
}