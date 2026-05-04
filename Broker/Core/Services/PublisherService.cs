using Broker.Contracts;
using Core.Abstractions;

namespace Core.Services;

public sealed class PublisherService(IMessageStorage messageStorage, IExchangeRouter exchangeRouter) : IPublisherService
{
    
    public async Task<PublishResponse> PublishAsync(
        PublishRequest request,
        CancellationToken ct = default)
    {
        if (request.Message is null || string.IsNullOrWhiteSpace(request.Message.Queue))
        {
            return new PublishResponse
            {
                Accepted = false,
                MessageId = request.Message?.Id ?? string.Empty,
                QueueName = request.Message?.Queue ?? string.Empty,
            };
        }

        var message = request.Message.Clone();

        if (string.IsNullOrWhiteSpace(message.Id))
            message.Id = Guid.NewGuid().ToString("N");

        if (message.Timestamp <= 0)
            message.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var targetQueues = await exchangeRouter.RouteAsync(message, ct);

        if (targetQueues.Count == 0)
        {
            return new PublishResponse
            {
                Accepted = false,
                MessageId = message.Id,
                QueueName = message.Queue
            };
        }

        var storedAny = false;

        foreach (var queueName in targetQueues)
        {
            var copy = message.Clone();
            copy.Queue = queueName;

            var stored = await messageStorage.TryStoreAsync(copy, queueName, ct);

            if (stored)
                storedAny = true;
        }

        return new PublishResponse
        {
            Accepted = storedAny,
            MessageId = message.Id,
            QueueName = string.Join(",", targetQueues)
        };
    }

    public async Task<PublishBatchResponse> PublishBatchAsync(
    PublishBatchRequest request,
    CancellationToken ct = default)
{
    var response = new PublishBatchResponse();

    if (request.Messages is null || request.Messages.Count == 0)
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
            response.Errors.Add("Message routing key is required.");
            continue;
        }

        var message = source.Clone();

        if (string.IsNullOrWhiteSpace(message.Id))
            message.Id = Guid.NewGuid().ToString("N");

        if (message.Timestamp <= 0)
            message.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var targetQueues = await exchangeRouter.RouteAsync(message, ct);

        if (targetQueues.Count == 0)
        {
            response.RejectedCount++;
            response.Errors.Add(
                $"No route for message '{message.Id}' with routing key '{message.Queue}'.");
            continue;
        }

        foreach (var queueName in targetQueues)
        {
            var copy = message.Clone();
            copy.Queue = queueName;

            if (!byQueue.TryGetValue(queueName, out var bucket))
            {
                bucket = [];
                byQueue[queueName] = bucket;
            }

            bucket.Add(copy);
        }
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