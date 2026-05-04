using Broker.Contracts;
using Core.Abstractions;
using PublisherService = Core.Services.PublisherService;

namespace Core.Tests.Services;

public class PublisherServiceTests
{
    private readonly Mock<IMessageStorage> _storageMock;
    private readonly Mock<IExchangeRouter> _routerMock;
    private readonly PublisherService _service;

    public PublisherServiceTests()
    {
        _storageMock = new Mock<IMessageStorage>();
        _routerMock = new Mock<IExchangeRouter>();
        _service = new PublisherService(_storageMock.Object,  _routerMock.Object);
        
    }

    [Fact]
    public async Task PublishAsync_DoesNotMutateOriginalMessage()
    {
        Message? storedMessage = null;
        var originalMessage = new Message
        {
            Queue = "orders"
        };

        _storageMock
            .Setup(s => s.TryStoreAsync(It.IsAny<Message>(), "orders", It.IsAny<CancellationToken>()))
            .Callback<Message, string, CancellationToken>((message, _, _) => storedMessage = message)
            .ReturnsAsync(true);

        await _service.PublishAsync(new PublishRequest { Message = originalMessage });

        Assert.NotNull(storedMessage);
        Assert.NotSame(originalMessage, storedMessage);
        Assert.Equal(string.Empty, originalMessage.Id);
        Assert.Equal(0, originalMessage.Timestamp);
    }

    [Fact]
    public async Task PublishAsync_WhenIdIsMissing_GeneratesId()
    {
        Message? storedMessage = null;

        _storageMock
            .Setup(s => s.TryStoreAsync(It.IsAny<Message>(), "orders", It.IsAny<CancellationToken>()))
            .Callback<Message, string, CancellationToken>((message, _, _) => storedMessage = message)
            .ReturnsAsync(true);

        var result = await _service.PublishAsync(new PublishRequest
        {
            Message = new Message
            {
                Queue = "orders",
                Timestamp = 123
            }
        });

        Assert.True(result.Accepted);
        Assert.NotNull(storedMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.MessageId));
        Assert.Equal(result.MessageId, storedMessage!.Id);
        Assert.Equal(123, storedMessage.Timestamp);
    }

    [Fact]
    public async Task PublishAsync_WhenMessageIsNull_ReturnsRejected_AndDoesNotCallStorage()
    {
        var result = await _service.PublishAsync(new PublishRequest());

        Assert.False(result.Accepted);
        Assert.Equal(string.Empty, result.MessageId);
        Assert.Equal(string.Empty, result.QueueName);
        _storageMock.Verify(
            s => s.TryStoreAsync(It.IsAny<Message>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public async Task PublishAsync_WhenQueueIsInvalid_ReturnsRejected_AndDoesNotCallStorage(string queue)
    {
        var request = new PublishRequest
        {
            Message = new Message
            {
                Id = "msg-1",
                Queue = queue
            }
        };

        var result = await _service.PublishAsync(request);

        Assert.False(result.Accepted);
        Assert.Equal("msg-1", result.MessageId);
        Assert.Equal(queue, result.QueueName);
        _storageMock.Verify(
            s => s.TryStoreAsync(It.IsAny<Message>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PublishAsync_WhenStorageReturnsFalse_ReturnsRejected()
    {
        _storageMock
            .Setup(s => s.TryStoreAsync(It.IsAny<Message>(), "orders", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.PublishAsync(new PublishRequest
        {
            Message = new Message
            {
                Id = "msg-1",
                Queue = "orders",
                Timestamp = 100
            }
        });

        Assert.False(result.Accepted);
        Assert.Equal("msg-1", result.MessageId);
        Assert.Equal("orders", result.QueueName);
    }

    [Fact]
    public async Task PublishAsync_WhenTimestampIsMissing_SetsTimestamp()
    {
        Message? storedMessage = null;

        _storageMock
            .Setup(s => s.TryStoreAsync(It.IsAny<Message>(), "orders", It.IsAny<CancellationToken>()))
            .Callback<Message, string, CancellationToken>((message, _, _) => storedMessage = message)
            .ReturnsAsync(true);

        var result = await _service.PublishAsync(new PublishRequest
        {
            Message = new Message
            {
                Id = "msg-1",
                Queue = "orders"
            }
        });

        Assert.True(result.Accepted);
        Assert.NotNull(storedMessage);
        Assert.Equal("msg-1", storedMessage!.Id);
        Assert.True(storedMessage.Timestamp > 0);
    }

    [Fact]
    public async Task PublishBatchAsync_DoesNotMutateOriginalMessages()
    {
        var originalOrders = new Message { Queue = "orders" };
        var originalPayments = new Message { Queue = "payments" };
        var request = new PublishBatchRequest
        {
            Messages = { originalOrders, originalPayments }
        };

        _storageMock
            .Setup(s => s.TryStoreBatchAsync(It.IsAny<IEnumerable<Message>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _service.PublishBatchAsync(request);

        Assert.Equal(string.Empty, originalOrders.Id);
        Assert.Equal(0, originalOrders.Timestamp);
        Assert.Equal(string.Empty, originalPayments.Id);
        Assert.Equal(0, originalPayments.Timestamp);
    }

    [Fact]
    public async Task PublishBatchAsync_GeneratesIdsAndTimestamps_ForValidMessages()
    {
        var storedMessages = new List<Message>();

        _storageMock
            .Setup(s => s.TryStoreBatchAsync(It.IsAny<IEnumerable<Message>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Message>, string, CancellationToken>((messages, _, _) => storedMessages.AddRange(messages))
            .ReturnsAsync(true);

        var result = await _service.PublishBatchAsync(new PublishBatchRequest
        {
            Messages =
            {
                new Message { Queue = "orders" },
                new Message { Queue = "orders", Id = "m2", Timestamp = 200 }
            }
        });

        Assert.Equal(2, result.AcceptedCount);
        Assert.Equal(0, result.RejectedCount);
        Assert.Equal(2, storedMessages.Count);
        Assert.False(string.IsNullOrWhiteSpace(storedMessages[0].Id));
        Assert.True(storedMessages[0].Timestamp > 0);
        Assert.Equal("m2", storedMessages[1].Id);
        Assert.Equal(200, storedMessages[1].Timestamp);
    }

    [Fact]
    public async Task PublishBatchAsync_GroupsMessagesByQueue_AndCallsStoragePerQueue()
    {
        var storedBatches = new Dictionary<string, List<Message>>(StringComparer.OrdinalIgnoreCase);

        _storageMock
            .Setup(s => s.TryStoreBatchAsync(It.IsAny<IEnumerable<Message>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Message>, string, CancellationToken>((messages, queue, _) =>
            {
                storedBatches[queue] = messages.ToList();
            })
            .ReturnsAsync(true);

        var result = await _service.PublishBatchAsync(new PublishBatchRequest
        {
            Messages =
            {
                new Message { Queue = "orders", Id = "m1", Timestamp = 100 },
                new Message { Queue = "payments", Id = "m2", Timestamp = 200 },
                new Message { Queue = "orders", Id = "m3", Timestamp = 300 }
            }
        });

        Assert.Equal(3, result.AcceptedCount);
        Assert.Equal(2, storedBatches.Count);
        Assert.Equal(2, storedBatches["orders"].Count);
        Assert.Single(storedBatches["payments"]);
        _storageMock.Verify(
            s => s.TryStoreBatchAsync(It.IsAny<IEnumerable<Message>>(), "orders", It.IsAny<CancellationToken>()),
            Times.Once);
        _storageMock.Verify(
            s => s.TryStoreBatchAsync(It.IsAny<IEnumerable<Message>>(), "payments", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PublishBatchAsync_WhenBatchIsEmpty_ReturnsError()
    {
        var result = await _service.PublishBatchAsync(new PublishBatchRequest());

        Assert.Equal(0, result.AcceptedCount);
        Assert.Equal(0, result.RejectedCount);
        Assert.Empty(result.MessageIds);
        Assert.Contains("Batch is empty.", result.Errors);
        _storageMock.Verify(
            s => s.TryStoreBatchAsync(It.IsAny<IEnumerable<Message>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PublishBatchAsync_WhenContainsInvalidMessages_AddsErrorsAndRejectedCount()
    {
        _storageMock
            .Setup(s => s.TryStoreBatchAsync(It.IsAny<IEnumerable<Message>>(), "orders", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.PublishBatchAsync(new PublishBatchRequest
        {
            Messages =
            {
                new Message { Queue = "orders", Id = "m1", Timestamp = 100 },
                new Message { Queue = "", Id = "m2", Timestamp = 200 },
                new Message { Queue = " ", Id = "m3", Timestamp = 300 }
            }
        });

        Assert.Equal(1, result.AcceptedCount);
        Assert.Equal(2, result.RejectedCount);
        Assert.Single(result.MessageIds);
        Assert.Equal("m1", result.MessageIds[0]);
        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, error => Assert.Equal("Message queue is required.", error));
    }

    [Fact]
    public async Task PublishBatchAsync_WhenStoreFailsForQueue_AddsRejectedCountAndError()
    {
        _storageMock
            .Setup(s => s.TryStoreBatchAsync(It.IsAny<IEnumerable<Message>>(), "orders", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _storageMock
            .Setup(s => s.TryStoreBatchAsync(It.IsAny<IEnumerable<Message>>(), "payments", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.PublishBatchAsync(new PublishBatchRequest
        {
            Messages =
            {
                new Message { Queue = "orders", Id = "m1", Timestamp = 100 },
                new Message { Queue = "orders", Id = "m2", Timestamp = 200 },
                new Message { Queue = "payments", Id = "m3", Timestamp = 300 }
            }
        });

        Assert.Equal(1, result.AcceptedCount);
        Assert.Equal(2, result.RejectedCount);
        Assert.Single(result.MessageIds);
        Assert.Equal("m3", result.MessageIds[0]);
        Assert.Contains("Failed to store messages for queue 'orders'.", result.Errors);
    }
}
