using Broker.Contracts;
using Core.Abstractions;
using Grpc.Core;

namespace Engine.Endpoints;

public sealed class MonitoringGrpcEndpoint(IMonitoringService service) : MonitoringService.MonitoringServiceBase
{
    public override Task<GetMetricsResponse> GetMetrics(GetMetricsRequest request, ServerCallContext context) =>
        service.GetMetricsAsync(request, context.CancellationToken);

    public override Task<BrokerStatus> GetBrokerStatus(GetBrokerStatusRequest request, ServerCallContext context) =>
        service.GetBrokerStatusAsync(request, context.CancellationToken);
}