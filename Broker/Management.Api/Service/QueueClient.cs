using Api.Connection;
using Broker.Contracts;

namespace Management.Api.MonitoringClient;

public class QueueClient(IBrokerConnection connection)
{
    private readonly QueueService.QueueServiceClient _client = connection.GetQueueClient();

    public async Task<ListQueuesResponse> ListQueues(CancellationToken ct)
    {
        return await _client.ListQueuesAsync(new ListQueuesRequest {}, cancellationToken: ct);
    }

    public async Task<CreateQueueResponse> CreateQueue(CreateQueueRequest request, CancellationToken ct)
    {
        return await _client.CreateQueueAsync(request, cancellationToken: ct);
    }

    public async Task<DeleteQueueResponse> DeleteQueue(DeleteQueueRequest request, CancellationToken ct)
    {
        return await _client.DeleteQueueAsync(request, cancellationToken: ct);
    }

    public async Task<GetQueueInfoResponse> GetQueueInfo(GetQueueInfoRequest request, CancellationToken ct)
    {
        return await _client.GetQueueInfoAsync(request, cancellationToken: ct);
    }

    public async Task<PurgeQueueResponse> PurgeQueue(PurgeQueueRequest request, CancellationToken ct)
    {
        return await _client.PurgeQueueAsync(request, cancellationToken: ct);
    }
}