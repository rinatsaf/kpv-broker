using Broker.Contracts;
using Grpc.Net.Client;

namespace Client.Connection;

public sealed class BrokerConnection : IBrokerConnection
{
    private readonly GrpcChannel _channel;

    public BrokerConnection(string address)
    {
        _channel = GrpcChannel.ForAddress(address);
    }

    public PublisherService.PublisherServiceClient GetPublisherClient() => 
        new(_channel);

    public ConsumerService.ConsumerServiceClient GetConsumerClient() => 
        new(_channel);

    public void Dispose() => _channel.Dispose();
}