using Broker.Contracts;
using Client.Configuration;
using Grpc.Net.Client;

namespace Client.Connection;

public sealed class BrokerConnection : IBrokerConnection
{
    private readonly GrpcChannel _channel;
    private readonly BrokerOptions _options;

    public BrokerConnection(BrokerOptions options)
    {
        _options = options;

        _channel = GrpcChannel.ForAddress(_options.Address);
    }
    
    public PublisherService.PublisherServiceClient GetPublisherClient() => 
        new(_channel);

    public ConsumerService.ConsumerServiceClient GetConsumerClient() => 
        new(_channel);

    public void Dispose() => _channel.Dispose();
}