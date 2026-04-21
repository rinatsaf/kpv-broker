using Broker.Contracts;
using Core.Abstractions;
using Grpc.Core;

namespace Engine.Endpoints;

public sealed class PublisherGrpcEndpoint(IPublisherService service) : PublisherService.PublisherServiceBase
{
    public override Task<PublishResponse> Publish(PublishRequest request, ServerCallContext context) =>
        service.PublishAsync(request, context.CancellationToken);

    public override Task<PublishBatchResponse> PublishBatch(PublishBatchRequest request, ServerCallContext context) =>
        service.PublishBatchAsync(request, context.CancellationToken);
}