using Broker.Contracts;
using Broker.Engine.Storage;

namespace Engine.MessageStorage;

public class MessageStorage : IMessageStorage
{
    public Task<bool> CreateQueueAsync(CreateQueueRequest request, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteQueueAsync(DeleteQueueRequest request, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    public Task<int> ExpireMessagesAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<Message>> FetchAsync(string queueName, string consumerGroup, string consumerId, int maxCount, TimeSpan visibilityTimeout, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<Message>> FetchFromDeadLetterAsync(string queueName, int maxCount, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<Message?> FetchOneAsync(string queueName, string consumerGroup, string consumerId, TimeSpan visibilityTimeout, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<GetMetricsResponse> GetMetricsAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<QueueInfo> GetQueueInfoAsync(GetQueueInfoRequest request, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<QueueStats> GetStatsAsync(string queueName, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<ListQueuesResponse> ListQueuesAsync(ListQueuesRequest request, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> MoveToDeadLetterAsync(Message message, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> PurgeQueueAsync(PurgeQueueRequest request, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> TryAcknowledgeAsync(AcknowledgeRequest request, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> TryRejectAsync(RejectRequest request, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> TryStoreAsync(Message message, string queueName, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> TryStoreBatchAsync(IEnumerable<Message> messages, string queueName, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}