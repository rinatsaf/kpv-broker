using Broker.Contracts;

namespace Client.Connection;

public interface IBrokerConnection : IDisposable
{
    PublisherService.PublisherServiceClient GetPublisherClient();
    ConsumerService.ConsumerServiceClient GetConsumerClient();
}