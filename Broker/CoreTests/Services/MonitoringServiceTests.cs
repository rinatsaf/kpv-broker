using Broker.Contracts;
using Core.Abstractions;
using MonitoringService = Core.Services.MonitoringService;

namespace Core.Tests.Services;

public class MonitoringServiceTests
{
    private readonly Mock<IMessageStorage> _storageMock;
    private readonly MonitoringService _service;

    public MonitoringServiceTests()
    {
        _storageMock = new Mock<IMessageStorage>();
        _service = new MonitoringService(_storageMock.Object);
    }

    [Fact]
    public async Task GetMetricsAsync_WhenRequestIsNull_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.GetMetricsAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public async Task GetMetricsAsync_WhenQueueNameIsBlank_ReturnsGlobalMetrics(string queueName)
    {
        var expected = new GetMetricsResponse
        {
            Metrics =
            {
                ["broker.messages.total"] = 42,
                ["timestamp"] = 123
            }
        };
        using var cts = new CancellationTokenSource();

        _storageMock
            .Setup(s => s.GetMetricsAsync(cts.Token))
            .ReturnsAsync(expected);

        var result = await _service.GetMetricsAsync(new GetMetricsRequest { QueueName = queueName }, cts.Token);

        Assert.Same(expected, result);
        _storageMock.Verify(s => s.GetMetricsAsync(cts.Token), Times.Once);
        _storageMock.Verify(s => s.GetStatsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMetricsAsync_WhenQueueNameProvided_ReturnsQueueMetrics()
    {
        var stats = new QueueStats
        {
            PublishedTotal = 10,
            ConsumedTotal = 8,
            AcknowledgedTotal = 7,
            RejectedTotal = 1,
            ExpiredTotal = 2,
            AvgProcessingTimeMs = 15.5
        };
        using var cts = new CancellationTokenSource();
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        _storageMock
            .Setup(s => s.GetStatsAsync("orders", cts.Token))
            .ReturnsAsync(stats);

        var result = await _service.GetMetricsAsync(new GetMetricsRequest { QueueName = "orders" }, cts.Token);

        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.Equal(7, result.Metrics.Count);
        Assert.Equal(10, result.Metrics["queue.orders.published"]);
        Assert.Equal(8, result.Metrics["queue.orders.consumed"]);
        Assert.Equal(7, result.Metrics["queue.orders.acknowledged"]);
        Assert.Equal(1, result.Metrics["queue.orders.rejected"]);
        Assert.Equal(2, result.Metrics["queue.orders.expired"]);
        Assert.Equal(15.5, result.Metrics["queue.orders.avg_processing_ms"]);
        Assert.InRange((long)result.Metrics["timestamp"], before, after);
        _storageMock.Verify(s => s.GetStatsAsync("orders", cts.Token), Times.Once);
        _storageMock.Verify(s => s.GetMetricsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetBrokerStatusAsync_WhenRequestIsNull_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.GetBrokerStatusAsync(null!));
    }

    [Fact]
    public async Task GetBrokerStatusAsync_WhenStorageSucceeds_ReturnsHealthyStatusWithTotals()
    {
        var queues = new ListQueuesResponse
        {
            Queues =
            {
                new QueueInfo { Name = "orders", MessageCount = 5 },
                new QueueInfo { Name = "payments", MessageCount = 7 },
                new QueueInfo { Name = "dlq", MessageCount = 2, IsDeadLetterQueue = true }
            }
        };
        using var cts = new CancellationTokenSource();

        _storageMock
            .Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), cts.Token))
            .ReturnsAsync(queues);

        var result = await _service.GetBrokerStatusAsync(new GetBrokerStatusRequest(), cts.Token);

        Assert.True(result.IsHealthy);
        Assert.Equal(3, result.TotalQueues);
        Assert.Equal(14, result.TotalMessages);
        Assert.Equal(0, result.ActiveConnections);
        Assert.True(result.UptimeSeconds >= 0);
        Assert.False(string.IsNullOrWhiteSpace(result.Version));
        _storageMock.Verify(
            s => s.ListQueuesAsync(It.Is<ListQueuesRequest>(request => request != null), cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task GetBrokerStatusAsync_WhenStorageThrowsNonCancellationException_ReturnsUnhealthyStatus()
    {
        using var cts = new CancellationTokenSource();

        _storageMock
            .Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), cts.Token))
            .ThrowsAsync(new InvalidOperationException("storage failure"));

        var result = await _service.GetBrokerStatusAsync(new GetBrokerStatusRequest(), cts.Token);

        Assert.False(result.IsHealthy);
        Assert.Equal(0, result.TotalQueues);
        Assert.Equal(0, result.TotalMessages);
        Assert.Equal(0, result.ActiveConnections);
        Assert.True(result.UptimeSeconds >= 0);
        Assert.False(string.IsNullOrWhiteSpace(result.Version));
    }

    [Fact]
    public async Task GetBrokerStatusAsync_WhenStorageThrowsOperationCanceledException_Propagates()
    {
        using var cts = new CancellationTokenSource();

        _storageMock
            .Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _service.GetBrokerStatusAsync(new GetBrokerStatusRequest(), cts.Token));
    }
}
