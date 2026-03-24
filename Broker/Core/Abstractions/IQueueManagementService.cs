using Broker.Contracts;

namespace Core.Abstractions;

public interface IQueueManagementService
{
    Task<CreateQueueResponse> CreateQueueAsync(
        CreateQueueRequest request,
        CancellationToken ct = default);

    Task<DeleteQueueResponse> DeleteQueueAsync(
        DeleteQueueRequest request,
        CancellationToken ct = default);

    Task<GetQueueInfoResponse> GetQueueInfoAsync(
        GetQueueInfoRequest request,
        CancellationToken ct = default);

    Task<ListQueuesResponse> ListQueuesAsync(
        ListQueuesRequest request,
        CancellationToken ct = default);

    Task<PurgeQueueResponse> PurgeQueueAsync(
        PurgeQueueRequest request,
        CancellationToken ct = default);
}