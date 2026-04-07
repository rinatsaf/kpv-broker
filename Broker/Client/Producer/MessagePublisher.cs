using Broker.Contracts;
using Client.Connection;

namespace Client.Producer;

public class MessagePublisher(IBrokerConnection connection) : IMessagePublisher
{
    public async Task<PublishResponse> PublishAsync(string queue, byte[] payload, IDictionary<string, string>? headers = null)
    {
        var message = new Message
        {
            Id = Guid.NewGuid().ToString("N"),
            Queue = queue,
            Payload = Google.Protobuf.ByteString.CopyFrom(payload),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        if (headers != null)
        {
            foreach (var (key, value) in headers) message.Headers.Add(key, value);
        }

        return await PublishAsync(message);
    }

    public async Task<PublishResponse> PublishAsync(Message message)
    {
        var client = connection.GetPublisherClient();
        return await client.PublishAsync(new PublishRequest { Message = message });
    }
}