using Broker.Contracts;

namespace Client.Producer;

public interface IMessagePublisher
{
    Task<PublishResponse> PublishAsync(string queue, byte[] payload, IDictionary<string, string>? headers = null);
    Task<PublishResponse> PublishAsync(Message message);
}