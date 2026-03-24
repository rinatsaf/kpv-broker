using Broker.Contracts;

namespace Core.Abstractions;

public interface IMonitoringService
{
    Task<GetMetricsResponse> GetMetricsAsync(
        GetMetricsRequest request,
        CancellationToken ct = default);

    Task<BrokerStatus> GetBrokerStatusAsync(
        GetBrokerStatusRequest request,
        CancellationToken ct = default);
}