using Broker.Contracts;

namespace Api.Connection;

public interface IBrokerConnection : IDisposable
{
    MonitoringService.MonitoringServiceClient GetMonitoringClient();
    ConfigService.ConfigServiceClient GetConfigClient();
}