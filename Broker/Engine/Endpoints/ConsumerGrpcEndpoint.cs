using Broker.Contracts;
using Core.Abstractions;
using Grpc.Core;

namespace Engine.Endpoints;

public sealed class ConsumerGrpcEndpoint(IConsumerService service) : ConsumerService.ConsumerServiceBase
{
    public override Task<ConsumeResponse> Consume(ConsumeRequest request, ServerCallContext context) =>
        service.ConsumeAsync(request, context.CancellationToken);

    public override Task<ConsumeBatchResponse> ConsumeBatch(ConsumeBatchRequest request, ServerCallContext context) =>
        service.ConsumeBatchAsync(request, context.CancellationToken);

    public override Task<AcknowledgeResponse> Acknowledge(AcknowledgeRequest request, ServerCallContext context) =>
        service.AcknowledgeAsync(request, context.CancellationToken);

    public override Task<RejectResponse> Reject(RejectRequest request, ServerCallContext context) =>
        service.RejectAsync(request, context.CancellationToken);

    public override async Task Subscribe(SubscribeRequest request, IServerStreamWriter<MessageEvent> responseStream, ServerCallContext context)
    {
        await foreach (var evt in service.SubscribeAsync(request, context.CancellationToken))
        {
            await responseStream.WriteAsync(evt);
        }
    }
}