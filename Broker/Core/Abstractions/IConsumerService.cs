using Broker.Contracts;

namespace Core.Abstractions;

public interface IConsumerService
{
    Task<ConsumeResponse> ConsumeAsync(
        ConsumeRequest request,
        CancellationToken ct = default);

    Task<ConsumeBatchResponse> ConsumeBatchAsync(
        ConsumeBatchRequest request,
        CancellationToken ct = default);

    Task<AcknowledgeResponse> AcknowledgeAsync(
        AcknowledgeRequest request,
        CancellationToken ct = default);

    Task<RejectResponse> RejectAsync(
        RejectRequest request,
        CancellationToken ct = default);

    IAsyncEnumerable<MessageEvent> SubscribeAsync(
        SubscribeRequest request,
        CancellationToken ct = default);
}