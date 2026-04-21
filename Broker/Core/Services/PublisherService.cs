using Broker.Contracts;
using Core.Abstractions;

namespace Core.Services;

public sealed class PublisherService(IMessageStorage messageStorage) : IPublisherService
{
    
    public async Task<PublishResponse> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        if (request.Message is null || string.IsNullOrWhiteSpace(request.Message.Queue))
        {
            return new  PublishResponse()
            {
                Accepted = false,
                MessageId = request.Message?.Id ??  string.Empty,
                QueueName = request.Message?.Queue ??  string.Empty,
            };
        }
        
        var message = request.Message.Clone();
        
        if (string.IsNullOrWhiteSpace(message.Id))
        {
            message.Id = Guid.NewGuid().ToString("N");
        }
        
        if (message.Timestamp <= 0)
        {
            message.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }


        var stored = await messageStorage.TryStoreAsync(
            message,
            message.Queue,
            ct);
            
        return new PublishResponse
        {
            Accepted = stored,
            MessageId = message.Id,
            QueueName = message.Queue
        };

    }

    public async Task<PublishBatchResponse> PublishBatchAsync(PublishBatchRequest request, CancellationToken ct = default)
    {
        var response = new PublishBatchResponse();
        
        if (request.Messages.Count == 0 || request.Messages is null)
        {
            response.Errors.Add("Batch is empty.");
            return response;
        }
        
        var byQueue = new Dictionary<string, List<Message>>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in request.Messages)
        {
            if (source is null)
            {
                response.RejectedCount++;
                response.Errors.Add("Message cannot be null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(source.Queue))
            {
                response.RejectedCount++;
                response.Errors.Add("Message queue is required.");
                continue;
            }
            
            var message = source.Clone();
            
            if (string.IsNullOrWhiteSpace(message.Id))
            {
                message.Id = Guid.NewGuid().ToString("N");
            }

            if (message.Timestamp <= 0)
            {
                message.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            
            if (!byQueue.TryGetValue(message.Queue, out var bucket))
            {
                bucket = [];
                byQueue[message.Queue] = bucket;
            }
            
            bucket.Add(message);
        }
        
        foreach (var pair in byQueue)
        {
            var stored = await messageStorage.TryStoreBatchAsync(pair.Value, pair.Key, ct);

            if (!stored)
            {
                response.RejectedCount += pair.Value.Count;
                response.Errors.Add($"Failed to store messages for queue '{pair.Key}'.");
                continue;
            }

            response.AcceptedCount += pair.Value.Count;

            foreach (var message in pair.Value)
            {
                response.MessageIds.Add(message.Id);
            }
        }

        return response;
    }
}