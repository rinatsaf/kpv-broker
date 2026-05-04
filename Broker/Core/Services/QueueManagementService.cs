using Broker.Contracts;
using Core.Abstractions;

namespace Core.Services;

public class QueueManagementService(IMessageStorage messageStorage) : IQueueManagementService
{
    public async Task<CreateQueueResponse> CreateQueueAsync(CreateQueueRequest request, CancellationToken ct = default)
    {
        var success = await messageStorage.CreateQueueAsync(request, ct);
        return new CreateQueueResponse
        {
            Success = success,
            QueueName = request.Name,
            Error = success ? "" : "Failed to create queue"
        };
    }

    public async Task<DeleteQueueResponse> DeleteQueueAsync(DeleteQueueRequest request, CancellationToken ct = default)
    {
        var success = await messageStorage.DeleteQueueAsync(request, ct);
        return new DeleteQueueResponse
        {
            Success = success
        };
    }

    public async Task<GetQueueInfoResponse> GetQueueInfoAsync(GetQueueInfoRequest request, CancellationToken ct = default)
    {
        var info = await messageStorage.GetQueueInfoAsync(request, ct);
        if (info == null)
            return new GetQueueInfoResponse { QueueFound = false };
        var dlq = await messageStorage.FetchFromDeadLetterAsync(info.Name, int.MaxValue, ct);
        var stats = await messageStorage.GetStatsAsync(info.Name, ct);
        
        return new GetQueueInfoResponse
        {
            QueueFound = true,
            Name = info.Name,
            MessageCount = info.MessageCount,
            DeadLetterCount = dlq.Count,
            Stats = stats
        };
    }

    public async Task<ListQueuesResponse> ListQueuesAsync(ListQueuesRequest request, CancellationToken ct = default)
    {
        return await messageStorage.ListQueuesAsync(request, ct);
    }

    public async Task<PurgeQueueResponse> PurgeQueueAsync(PurgeQueueRequest request, CancellationToken ct = default)
    {
        var req = new GetQueueInfoRequest
        {
            Name = request.Name,
        };
        
        var infoBeforePurge = await messageStorage.GetQueueInfoAsync(req, ct);
        if (infoBeforePurge == null)
            return new PurgeQueueResponse { QueueFound = false };

        var resp =  await messageStorage.PurgeQueueAsync(request, ct);
        
        if (resp)
        {
            var infoAfterPurge = await messageStorage.GetQueueInfoAsync(req, ct);
            if (infoAfterPurge == null)
                return new PurgeQueueResponse { QueueFound = false };
            return new PurgeQueueResponse
            {
                QueueFound = true,
                MessagesRemoved = infoBeforePurge.MessageCount - infoAfterPurge.MessageCount
            };
        }
        return new PurgeQueueResponse
        {
            MessagesRemoved = 0
        };
    }
}
