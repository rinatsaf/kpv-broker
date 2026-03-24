using Broker.Contracts;

namespace Core.Abstractions;

public interface IPublisherService
{
    Task<PublishResponse> PublishAsync(
        PublishRequest request,
        CancellationToken ct = default);

    Task<PublishBatchResponse> PublishBatchAsync(
        PublishBatchRequest request,
        CancellationToken ct = default);
}