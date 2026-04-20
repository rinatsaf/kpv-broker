using Broker.Contracts;
using Client.Connection;
using Google.Protobuf.Collections;

namespace Client.Producer;

public class MessagePublisher(IBrokerConnection connection) : IMessagePublisher
{
    private readonly PublisherService.PublisherServiceClient _client = connection.GetPublisherClient();

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
            foreach (var (key, value) in headers)
            {
                message.Headers.Add(key, value);
            }
        }

        return await PublishAsync(message);
    }

    public async Task<PublishResponse> PublishAsync(Message message)
    {
        return await _client.PublishAsync(new PublishRequest { Message = message });
    }

    public async Task<PublishBatchResponse> PublishBatchAsync(string queue, IEnumerable<byte[]> payloads, IDictionary<string, string>? headers = null)
    {
        var request = new PublishBatchRequest();
        foreach (var p in payloads)
        {
            var mes = new Message
            {
                Id = Guid.NewGuid().ToString("N"),
                Queue = queue,
                Payload = Google.Protobuf.ByteString.CopyFrom(p),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            if (headers != null)
            {
                foreach (var (key, value) in headers)
                {
                    mes.Headers.Add(key, value);
                }
            }

            request.Messages.Add(mes);
        }

        return await _client.PublishBatchAsync(request);
    }

    public async Task<PublishBatchResponse> PublishBatchAsync(IEnumerable<Message> messages)
    {
        var request = new PublishBatchRequest();
        request.Messages.AddRange(messages);
        return await _client.PublishBatchAsync(request);
    }
}