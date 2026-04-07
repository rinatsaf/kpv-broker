using Broker.Contracts;
using Grpc.Net.Client;


namespace Client.Connection;


public interface IBrokerConnection : IDisposable
{
    PublisherService.PublisherServiceClient GetPublisherClient();
    ConsumerService.ConsumerServiceClient GetConsumerClient();
}