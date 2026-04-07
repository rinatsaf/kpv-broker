using Broker.Contracts;
using Core.Abstractions;
using Moq;
using QueueManagementService = Core.Services.QueueManagementService;

namespace Core.Tests.Services;

public class QueueManagementServiceTests
{
    private readonly Mock<IMessageStorage> _storageMock;
    private readonly QueueManagementService _service;

    public QueueManagementServiceTests()
    {
        _storageMock = new Mock<IMessageStorage>();
        _service = new QueueManagementService(_storageMock.Object);
    }

    [Fact]
    public async Task CreateQueueAsync_WhenStorageReturnsTrue_ReturnsSuccessfulResponse()
    {
        var request = new CreateQueueRequest { Name = "orders" };
        var ct = new CancellationTokenSource().Token;

        _storageMock
            .Setup(s => s.CreateQueueAsync(request, ct))
            .ReturnsAsync(true);

        var result = await _service.CreateQueueAsync(request, ct);

        Assert.True(result.Success);
        Assert.Equal("orders", result.QueueName);
        Assert.Equal(string.Empty, result.Error);
        _storageMock.Verify(s => s.CreateQueueAsync(request, ct), Times.Once);
    }

    [Fact]
    public async Task CreateQueueAsync_WhenStorageReturnsFalse_ReturnsErrorResponse()
    {
        var request = new CreateQueueRequest { Name = "orders" };

        _storageMock
            .Setup(s => s.CreateQueueAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.CreateQueueAsync(request);

        Assert.False(result.Success);
        Assert.Equal("orders", result.QueueName);
        Assert.Equal("Failed to create queue", result.Error);
        _storageMock.Verify(s => s.CreateQueueAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeleteQueueAsync_ReturnsStorageResult(bool deleted)
    {
        var request = new DeleteQueueRequest { Name = "orders" };
        var ct = new CancellationTokenSource().Token;

        _storageMock
            .Setup(s => s.DeleteQueueAsync(request, ct))
            .ReturnsAsync(deleted);

        var result = await _service.DeleteQueueAsync(request, ct);

        Assert.Equal(deleted, result.Success);
        _storageMock.Verify(s => s.DeleteQueueAsync(request, ct), Times.Once);
    }

    [Fact]
    public async Task GetQueueInfoAsync_ReturnsInfoWithDeadLetterCountAndStats()
    {
        var request = new GetQueueInfoRequest { Name = "orders" };
        var info = new QueueInfo
        {
            Name = "orders",
            MessageCount = 12
        };
        var deadLetters = new List<Message>
        {
            new(),
            new(),
            new()
        };
        var stats = new QueueStats
        {
            PublishedTotal = 100,
            ConsumedTotal = 50,
            AcknowledgedTotal = 40,
            RejectedTotal = 10,
            ExpiredTotal = 2,
            AvgProcessingTimeMs = 15.5
        };
        var ct = new CancellationTokenSource().Token;

        _storageMock
            .Setup(s => s.GetQueueInfoAsync(request, ct))
            .ReturnsAsync(info);
        _storageMock
            .Setup(s => s.FetchFromDeadLetterAsync("orders", int.MaxValue, ct))
            .ReturnsAsync(deadLetters);
        _storageMock
            .Setup(s => s.GetStatsAsync("orders", ct))
            .ReturnsAsync(stats);

        var result = await _service.GetQueueInfoAsync(request, ct);

        Assert.Equal("orders", result.Name);
        Assert.Equal(12, result.MessageCount);
        Assert.Equal(3, result.DeadLetterCount);
        Assert.Same(stats, result.Stats);
        _storageMock.Verify(s => s.GetQueueInfoAsync(request, ct), Times.Once);
        _storageMock.Verify(s => s.FetchFromDeadLetterAsync("orders", int.MaxValue, ct), Times.Once);
        _storageMock.Verify(s => s.GetStatsAsync("orders", ct), Times.Once);
    }

    [Fact]
    public async Task ListQueuesAsync_ReturnsStorageResponse()
    {
        var request = new ListQueuesRequest();
        var response = new ListQueuesResponse
        {
            Queues =
            {
                new QueueInfo { Name = "orders", MessageCount = 2 },
                new QueueInfo { Name = "payments", MessageCount = 5 }
            }
        };
        var ct = new CancellationTokenSource().Token;

        _storageMock
            .Setup(s => s.ListQueuesAsync(request, ct))
            .ReturnsAsync(response);

        var result = await _service.ListQueuesAsync(request, ct);

        Assert.Same(response, result);
        Assert.Equal(2, result.Queues.Count);
        _storageMock.Verify(s => s.ListQueuesAsync(request, ct), Times.Once);
    }

    [Fact]
    public async Task PurgeQueueAsync_WhenStorageReturnsFalse_ReturnsZeroAndDoesNotReloadQueueInfo()
    {
        var request = new PurgeQueueRequest { Name = "orders" };
        var infoBeforePurge = new QueueInfo
        {
            Name = "orders",
            MessageCount = 10
        };
        var expectedInfoRequest = new GetQueueInfoRequest { Name = "orders" };
        var ct = new CancellationTokenSource().Token;

        _storageMock
            .Setup(s => s.GetQueueInfoAsync(
                It.Is<GetQueueInfoRequest>(r => r.Name == "orders"),
                ct))
            .ReturnsAsync(infoBeforePurge);
        _storageMock
            .Setup(s => s.PurgeQueueAsync(request, ct))
            .ReturnsAsync(false);

        var result = await _service.PurgeQueueAsync(request, ct);

        Assert.Equal(0, result.MessagesRemoved);
        _storageMock.Verify(
            s => s.GetQueueInfoAsync(
                It.Is<GetQueueInfoRequest>(r => r.Name == expectedInfoRequest.Name),
                ct),
            Times.Once);
        _storageMock.Verify(s => s.PurgeQueueAsync(request, ct), Times.Once);
    }

    [Fact]
    public async Task PurgeQueueAsync_WhenStorageReturnsTrue_ReturnsRemovedMessagesCount()
    {
        var request = new PurgeQueueRequest { Name = "orders" };
        var before = new QueueInfo
        {
            Name = "orders",
            MessageCount = 10
        };
        var after = new QueueInfo
        {
            Name = "orders",
            MessageCount = 4
        };
        var ct = new CancellationTokenSource().Token;

        _storageMock
            .SetupSequence(s => s.GetQueueInfoAsync(
                It.Is<GetQueueInfoRequest>(r => r.Name == "orders"),
                ct))
            .ReturnsAsync(before)
            .ReturnsAsync(after);
        _storageMock
            .Setup(s => s.PurgeQueueAsync(request, ct))
            .ReturnsAsync(true);

        var result = await _service.PurgeQueueAsync(request, ct);

        Assert.Equal(6, result.MessagesRemoved);
        _storageMock.Verify(
            s => s.GetQueueInfoAsync(
                It.Is<GetQueueInfoRequest>(r => r.Name == "orders"),
                ct),
            Times.Exactly(2));
        _storageMock.Verify(s => s.PurgeQueueAsync(request, ct), Times.Once);
    }
}
