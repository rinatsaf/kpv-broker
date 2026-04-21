using Broker.Contracts;
using Core.Abstractions;
using Grpc.Core;

namespace Engine.Endpoints;

public sealed class QueueGrpcEndpoint(IQueueManagementService service) : QueueService.QueueServiceBase
{
    public override Task<CreateQueueResponse> CreateQueue(CreateQueueRequest request, ServerCallContext context) =>
        service.CreateQueueAsync(request, context.CancellationToken);

    public override Task<DeleteQueueResponse> DeleteQueue(DeleteQueueRequest request, ServerCallContext context) =>
        service.DeleteQueueAsync(request, context.CancellationToken);

    public override Task<GetQueueInfoResponse> GetQueueInfo(GetQueueInfoRequest request, ServerCallContext context) =>
        service.GetQueueInfoAsync(request, context.CancellationToken);

    public override Task<ListQueuesResponse> ListQueues(ListQueuesRequest request, ServerCallContext context) =>
        service.ListQueuesAsync(request, context.CancellationToken);

    public override Task<PurgeQueueResponse> PurgeQueue(PurgeQueueRequest request, ServerCallContext context) =>
        service.PurgeQueueAsync(request, context.CancellationToken);
}