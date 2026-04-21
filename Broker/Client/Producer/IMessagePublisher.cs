using System.Text;
using Broker.Contracts;

namespace Client.Producer;

public interface IMessagePublisher
{
    //Publish
    Task<PublishResponse> PublishAsync(string queue, string content, IDictionary<string, string>? headers = null) =>
        PublishAsync(queue, Encoding.UTF8.GetBytes(content), headers);

    Task<PublishResponse> PublishAsync(string queue, byte[] payload, IDictionary<string, string>? headers = null);
    Task<PublishResponse> PublishAsync(Message message);

    //PublishBatch
    Task<PublishBatchResponse> PublishBatchAsync(string queue, IEnumerable<string> contents, IDictionary<string, string>? headers = null) =>
        PublishBatchAsync(queue, contents.Select(Encoding.UTF8.GetBytes), headers);
    
    Task<PublishBatchResponse> PublishBatchAsync(string queue, IEnumerable<byte[]> payloads, IDictionary<string, string>? headers = null);
    Task<PublishBatchResponse> PublishBatchAsync(IEnumerable<Message> message);
}