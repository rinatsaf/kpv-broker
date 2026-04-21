using System.Text;
using Broker.Contracts;
using Engine.MessageStorage;
using Google.Protobuf;

namespace Core.Tests.MessageStorage;

public sealed class FileMessageStorageTests
{
    [Fact]
    public async Task QueueLifecycle_CreateListInfoPurgeDelete_WorksEndToEnd()
    {
        await using var ctx = new StorageTestContext();
        var storage = ctx.Storage;
        const string queue = "orders";

        var created = await storage.CreateQueueAsync(new CreateQueueRequest
        {
            Name = queue,
            MaxDeliveryAttempts = 3
        });
        Assert.True(created);

        var list = await storage.ListQueuesAsync(new ListQueuesRequest());
        Assert.Contains(list.Queues, q => q.Name == queue);

        var stored = await storage.TryStoreAsync(NewMessage(queue), queue);
        Assert.True(stored);

        var infoBeforePurge = await storage.GetQueueInfoAsync(new GetQueueInfoRequest { Name = queue });
        Assert.Equal(queue, infoBeforePurge.Name);
        Assert.Equal(1, infoBeforePurge.MessageCount);

        var purged = await storage.PurgeQueueAsync(new PurgeQueueRequest { Name = queue });
        Assert.True(purged);

        var fetchedAfterPurge = await storage.FetchAsync(queue, "cg", "c1", 10, TimeSpan.FromSeconds(5));
        Assert.Empty(fetchedAfterPurge);

        var deleted = await storage.DeleteQueueAsync(new DeleteQueueRequest { Name = queue });
        Assert.True(deleted);
    }

    [Fact]
    public async Task TryStoreAsync_UsesMessageQueue_WhenQueueNameIsWhitespace()
    {
        await using var ctx = new StorageTestContext();
        var storage = ctx.Storage;
        const string queue = "fallback-q";

        await storage.CreateQueueAsync(new CreateQueueRequest { Name = queue });
        var result = await storage.TryStoreAsync(NewMessage(queue), "   ");

        Assert.True(result);
        var fetched = await storage.FetchOneAsync(queue, "cg", "c1", TimeSpan.FromSeconds(5));
        Assert.NotNull(fetched);
    }

    [Fact]
    public async Task TryStoreAsync_ReturnsFalse_WhenTargetQueueIsEmpty()
    {
        await using var ctx = new StorageTestContext();
        var storage = ctx.Storage;

        var result = await storage.TryStoreAsync(NewMessage(queue: ""), "");

        Assert.False(result);
    }

    [Fact]
    public async Task TryStoreBatchAsync_EmptyBatch_ReturnsTrue()
    {
        await using var ctx = new StorageTestContext();
        var storage = ctx.Storage;

        var result = await storage.TryStoreBatchAsync([], "orders");

        Assert.True(result);
    }

    [Fact]
    public async Task TryStoreBatchAsync_UsesFirstMessageQueue_WhenQueueNameIsWhitespace()
    {
        await using var ctx = new StorageTestContext();
        var storage = ctx.Storage;
        const string queue = "batch-q";

        await storage.CreateQueueAsync(new CreateQueueRequest { Name = queue });
        var batch = new[]
        {
            NewMessage(queue, "m-1"),
            NewMessage(queue, "m-2")
        };

        var stored = await storage.TryStoreBatchAsync(batch, " ");

        Assert.True(stored);
        var fetched = await storage.FetchAsync(queue, "cg", "c1", 10, TimeSpan.FromSeconds(5));
        Assert.Equal(2, fetched.Count);
    }

    [Fact]
    public async Task FetchOneAsync_ReturnsNull_WhenQueueHasNoMessages()
    {
        await using var ctx = new StorageTestContext();
        var storage = ctx.Storage;
        const string queue = "empty-q";

        await storage.CreateQueueAsync(new CreateQueueRequest { Name = queue });
        var msg = await storage.FetchOneAsync(queue, "cg", "c1", TimeSpan.FromSeconds(5));

        Assert.Null(msg);
    }

    [Fact]
    public async Task FetchAndAcknowledge_RemovesMessageAndUpdatesStats()
    {
        await using var ctx = new StorageTestContext();
        var storage = ctx.Storage;
        const string queue = "ack-q";

        await storage.CreateQueueAsync(new CreateQueueRequest { Name = queue });
        await storage.TryStoreAsync(NewMessage(queue, "ack-1"), queue);

        var fetched = await storage.FetchOneAsync(queue, "cg", "consumer-1", TimeSpan.FromSeconds(10));
        Assert.NotNull(fetched);

        var acked = await storage.TryAcknowledgeAsync(new AcknowledgeRequest
        {
            MessageId = fetched!.Id,
            ConsumerId = "consumer-1"
        });
        Assert.True(acked);

        var refetch = await storage.FetchOneAsync(queue, "cg", "consumer-1", TimeSpan.FromSeconds(10));
        Assert.Null(refetch);

        var stats = await storage.GetStatsAsync(queue);
        Assert.Equal(1, stats.PublishedTotal);
        Assert.Equal(1, stats.ConsumedTotal);
        Assert.Equal(1, stats.AcknowledgedTotal);
    }

    [Fact]
    public async Task TryAcknowledgeAsync_ReturnsFalse_ForWrongConsumer()
    {
        await using var ctx = new StorageTestContext();
        var storage = ctx.Storage;
        const string queue = "ack-wrong-consumer-q";

        await storage.CreateQueueAsync(new CreateQueueRequest { Name = queue });
        await storage.TryStoreAsync(NewMessage(queue, "ack-2"), queue);

        var fetched = await storage.FetchOneAsync(queue, "cg", "consumer-1", TimeSpan.FromSeconds(10));
        Assert.NotNull(fetched);

        var acked = await storage.TryAcknowledgeAsync(new AcknowledgeRequest
        {
            MessageId = fetched!.Id,
            ConsumerId = "consumer-2"
        });
        Assert.False(acked);
    }

    [Fact]
    public async Task TryRejectAsync_WithRequeueFalse_MovesMessageToDeadLetterState()
    {
        await using var ctx = new StorageTestContext();
        var storage = ctx.Storage;
        const string queue = "reject-dlq-q";

        await storage.CreateQueueAsync(new CreateQueueRequest
        {
            Name = queue,
            DeadLetterEnabled = true,
            DeadLetterQueue = queue
        });
        await storage.TryStoreAsync(NewMessage(queue, "rej-1"), queue);

        var fetched = await storage.FetchOneAsync(queue, "cg", "consumer-1", TimeSpan.FromSeconds(10));
        Assert.NotNull(fetched);

        var rejected = await storage.TryRejectAsync(new RejectRequest
        {
            MessageId = fetched!.Id,
            ConsumerId = "consumer-1",
            Requeue = false,
            Reason = "boom"
        });
        Assert.True(rejected);

        var dlqMessages = await storage.FetchFromDeadLetterAsync(queue, 10);
        Assert.Contains(dlqMessages, m => m.Id == fetched.Id);
    }

    [Fact]
    public async Task TryRejectAsync_ReturnsFalse_WhenMessageNotFound()
    {
        await using var ctx = new StorageTestContext();
        var storage = ctx.Storage;
        const string queue = "reject-not-found-q";

        await storage.CreateQueueAsync(new CreateQueueRequest { Name = queue });

        var rejected = await storage.TryRejectAsync(new RejectRequest
        {
            MessageId = "missing-id",
            ConsumerId = "consumer-1",
            Requeue = false
        });

        Assert.False(rejected);
    }

    [Fact]
    public async Task MoveToDeadLetterAsync_ReturnsTrue_AndMessageVisibleInDeadLetterFetch()
    {
        await using var ctx = new StorageTestContext();
        var storage = ctx.Storage;
        const string queue = "manual-dlq-q";

        await storage.CreateQueueAsync(new CreateQueueRequest
        {
            Name = queue,
            DeadLetterEnabled = true,
            DeadLetterQueue = queue
        });

        var message = NewMessage(queue, "manual-dlq-1");
        message.Headers["LastError"] = "manual failure";
        await storage.TryStoreAsync(message, queue);

        var moved = await storage.MoveToDeadLetterAsync(message);
        Assert.True(moved);

        var dlqMessages = await storage.FetchFromDeadLetterAsync(queue, 10);
        Assert.Contains(dlqMessages, m => m.Id == message.Id);
    }

    [Fact]
    public async Task MoveToDeadLetterAsync_ReturnsFalse_WhenDeadLetterDisabled()
    {
        await using var ctx = new StorageTestContext();
        var storage = ctx.Storage;
        const string queue = "dlq-disabled-q";

        await storage.CreateQueueAsync(new CreateQueueRequest
        {
            Name = queue,
            DeadLetterEnabled = false
        });

        var message = NewMessage(queue, "dlq-disabled-1");
        await storage.TryStoreAsync(message, queue);

        var moved = await storage.MoveToDeadLetterAsync(message);
        Assert.False(moved);
    }

    [Fact]
    public async Task FetchFromDeadLetterAsync_RespectsMaxCount()
    {
        await using var ctx = new StorageTestContext();
        var storage = ctx.Storage;
        const string queue = "dlq-maxcount-q";

        await storage.CreateQueueAsync(new CreateQueueRequest
        {
            Name = queue,
            DeadLetterEnabled = true,
            DeadLetterQueue = queue
        });

        var m1 = NewMessage(queue, "dlq-max-1");
        var m2 = NewMessage(queue, "dlq-max-2");
        await storage.TryStoreAsync(m1, queue);
        await storage.TryStoreAsync(m2, queue);
        await storage.MoveToDeadLetterAsync(m1);
        await storage.MoveToDeadLetterAsync(m2);

        var dlqMessages = await storage.FetchFromDeadLetterAsync(queue, 1);
        Assert.Single(dlqMessages);
    }

    [Fact]
    public async Task ExpireMessagesAsync_RemovesExpiredMessages()
    {
        await using var ctx = new StorageTestContext();
        var storage = ctx.Storage;
        const string queue = "ttl-q";

        await storage.CreateQueueAsync(new CreateQueueRequest
        {
            Name = queue,
            MessageTtlSeconds = 1
        });
        await storage.TryStoreAsync(NewMessage(queue, "ttl-1"), queue);

        await Task.Delay(TimeSpan.FromSeconds(2));
        var expired = await storage.ExpireMessagesAsync();

        Assert.Equal(1, expired);
        var fetched = await storage.FetchAsync(queue, "cg", "consumer-1", 10, TimeSpan.FromSeconds(5));
        Assert.Empty(fetched);

        var stats = await storage.GetStatsAsync(queue);
        Assert.True(stats.ExpiredTotal >= 1);
    }

    [Fact]
    public async Task ExpireMessagesAsync_RequeuesTimedOutInFlightMessages()
    {
        await using var ctx = new StorageTestContext();
        var storage = ctx.Storage;
        const string queue = "requeue-inflight-q";

        await storage.CreateQueueAsync(new CreateQueueRequest { Name = queue });
        await storage.TryStoreAsync(NewMessage(queue, "inflight-1"), queue);

        var firstFetch = await storage.FetchOneAsync(queue, "cg", "consumer-1", TimeSpan.Zero);
        Assert.NotNull(firstFetch);

        var secondFetchBeforeCleanup = await storage.FetchOneAsync(queue, "cg", "consumer-2", TimeSpan.Zero);
        Assert.Null(secondFetchBeforeCleanup);

        await storage.ExpireMessagesAsync();

        var thirdFetchAfterCleanup = await storage.FetchOneAsync(queue, "cg", "consumer-2", TimeSpan.Zero);
        Assert.NotNull(thirdFetchAfterCleanup);
        Assert.Equal(firstFetch!.Id, thirdFetchAfterCleanup!.Id);
    }

    [Fact]
    public async Task GetMetricsAsync_ReturnsQueueAndTotalMetrics()
    {
        await using var ctx = new StorageTestContext();
        var storage = ctx.Storage;
        const string queue = "metrics-q";

        await storage.CreateQueueAsync(new CreateQueueRequest { Name = queue });
        await storage.TryStoreAsync(NewMessage(queue, "metrics-1"), queue);

        var metrics = await storage.GetMetricsAsync();

        Assert.True(metrics.Metrics.ContainsKey($"queue.{queue}.published"));
        Assert.True(metrics.Metrics.ContainsKey("total.queues"));
        Assert.True(metrics.Metrics.ContainsKey("total.messages.published"));
        Assert.True(metrics.Metrics.ContainsKey("timestamp"));
        Assert.True(metrics.Metrics[$"queue.{queue}.published"] >= 1);
    }

    [Fact]
    public async Task MethodsThrowObjectDisposedException_AfterDispose()
    {
        var root = Path.Combine(Path.GetTempPath(), "fms-tests-" + Guid.NewGuid().ToString("N"));
        await using var storage = new FileMessageStorage(root);
        await storage.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            storage.GetMetricsAsync());
    }

    private static Message NewMessage(string queue, string? id = null, int ttlSeconds = 0)
    {
        return new Message
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Queue = queue,
            Payload = ByteString.CopyFrom(Encoding.UTF8.GetBytes("payload")),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            TtlSeconds = ttlSeconds
        };
    }

    private sealed class StorageTestContext : IAsyncDisposable
    {
        public string RootPath { get; }
        public FileMessageStorage Storage { get; }

        public StorageTestContext()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "fms-tests-" + Guid.NewGuid().ToString("N"));
            Storage = new FileMessageStorage(RootPath);
        }

        public async ValueTask DisposeAsync()
        {
            await Storage.DisposeAsync();
            try
            {
                if (Directory.Exists(RootPath))
                    Directory.Delete(RootPath, recursive: true);
            }
            catch
            {

            }
        }
    }
}
