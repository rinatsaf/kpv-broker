using Api.Connection;
using Broker.Contracts;

namespace Api.MonitoringClient;

public class MonitoringClient(IBrokerConnection connection)
{
    private readonly MonitoringService.MonitoringServiceClient _client = connection.GetMonitoringClient();

    public async Task<BrokerStatus> GetBrokerStatus(CancellationToken ct)
    {
        return await _client.GetBrokerStatusAsync(new GetBrokerStatusRequest { }, cancellationToken: ct);
    }

    public async Task<GetMetricsResponse> GetMetricsAsync(string queueName, long from, long to, CancellationToken ct)
    {
        return await _client.GetMetricsAsync(new GetMetricsRequest
        {
            QueueName = queueName,
            FromTimestamp = from,
            ToTimestamp = to,
        }, cancellationToken: ct);
    }
}