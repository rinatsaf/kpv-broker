using System.Runtime.CompilerServices;
using Broker.Contracts;
using Core.Abstractions;

namespace Core.Services;

public sealed class ConsumerService(IMessageStorage messageStorage) : IConsumerService
{
    private static readonly TimeSpan VisibilityTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EmptyQueueDelay = TimeSpan.FromMilliseconds(250);
    
    public async Task<ConsumeResponse> ConsumeAsync(ConsumeRequest request, CancellationToken ct = default)
    {
        // Валидация
        if (string.IsNullOrWhiteSpace(request.ConsumerId) ||
            string.IsNullOrWhiteSpace(request.Queue) ||
            string.IsNullOrWhiteSpace(request.ConsumerGroup))
        {
            return new ConsumeResponse();
        }
        
        var maxMessages = request.MaxMessages > 0 ? request.MaxMessages : 1;

        // Получаем сообщения из хранилища
        var messages = await messageStorage.FetchAsync(
            request.Queue,
            request.ConsumerGroup,
            request.ConsumerId,
            maxMessages,
            VisibilityTimeout,
            ct);
        
        var response = new ConsumeResponse()
        {
            ConsumerId = request.ConsumerId,
        };
        
        response.Messages.AddRange(messages);
        
        return response;
    }

    public async Task<AcknowledgeResponse> AcknowledgeAsync(AcknowledgeRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId) ||
            string.IsNullOrWhiteSpace(request.ConsumerId))
        {
            return new AcknowledgeResponse
            {
                Acknowledged = false,
                MessageId = request.MessageId ?? string.Empty
            };
        }
        var acknowledged = await messageStorage.TryAcknowledgeAsync(request, ct);

        return new AcknowledgeResponse
        {
            Acknowledged = acknowledged,
            MessageId = request.MessageId
        };
    }

    public async Task<RejectResponse> RejectAsync(RejectRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId) ||
            string.IsNullOrWhiteSpace(request.ConsumerId))
        {
            return new RejectResponse
            {
                Rejected = false,
                MessageId = request.MessageId ?? string.Empty,
                MovedToDlq = false
            };
        }

        var rejected = await messageStorage.TryRejectAsync(request, ct);

        return new RejectResponse
        {
            Rejected = rejected,
            MessageId = request.MessageId,
            MovedToDlq = rejected && !request.Requeue
        };
    }

    public async IAsyncEnumerable<MessageEvent> SubscribeAsync(SubscribeRequest request,[EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Queue) ||
            string.IsNullOrWhiteSpace(request.ConsumerGroup) ||
            string.IsNullOrWhiteSpace(request.ConsumerId))
        {
            yield break;
        }
        
        var prefetchCount = request.PrefetchCount > 0 ? request.PrefetchCount : 1;
        
        while (!ct.IsCancellationRequested)
        {
            var messages = await messageStorage.FetchAsync(
                request.Queue,
                request.ConsumerGroup,
                request.ConsumerId,
                prefetchCount,
                VisibilityTimeout,
                ct);

            if (messages.Count == 0)
            {
                await Task.Delay(EmptyQueueDelay, ct);
                continue;
            }

            foreach (var message in messages)
            {
                yield return new MessageEvent
                {
                    Message = message,
                    QueueName = request.Queue,
                    ReceivedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
            }
        }
    }
}