using Broker.Contracts;
using Grpc.Net.Client;

namespace Api.Connection;

public sealed class BrokerConnection(string address) : IBrokerConnection
{
    private readonly GrpcChannel _channel = GrpcChannel.ForAddress(address);

    public MonitoringService.MonitoringServiceClient GetMonitoringClient() =>
        new(_channel);
    
    public QueueService.QueueServiceClient GetQueueClient() =>
        new(_channel);
        
    public ConfigService.ConfigServiceClient GetConfigClient() => 
        new(_channel);

    public void Dispose() => _channel.Dispose();
}