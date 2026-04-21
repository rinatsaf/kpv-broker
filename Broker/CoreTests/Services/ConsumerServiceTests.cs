using Broker.Contracts;
using Core.Abstractions;
using ConsumerService = Core.Services.ConsumerService;

namespace Core.Tests.Services;

public class ConsumerServiceTests
{
    private readonly Mock<IMessageStorage> _storageMock;
    private readonly ConsumerService _service;

    public ConsumerServiceTests()
    {
        _storageMock = new Mock<IMessageStorage>();
        _service = new ConsumerService(_storageMock.Object);
    }

    [Fact] // тест на валидацию
    public async Task ConsumeAsync_WhenRequestInvalid_ShouldReturnEmptyResponse()
    {
        var request = new ConsumeRequest { ConsumerId = "" }; // Пустой ID

        var result = await _service.ConsumeAsync(request);
        
        Assert.NotNull(result);
        // проверка, обращались ли к хранилищу
        _storageMock.Verify(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string>(), 
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), 
            Times.Never);
    }

    [Fact] // тест на корректную передачу данных в хранилище ()
    public async Task ConsumeBatchAsync_WhenMaxMessagesIsZero_ShouldUseOne()
    {
        var request = new ConsumeBatchRequest 
        { 
            ConsumerId = "c1", Queue = "q1", ConsumerGroup = "g1", MaxMessages = 0
        };
        
        _storageMock.Setup(s => s.FetchAsync("q1", "g1", "c1", 1, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Message>());
        
        await _service.ConsumeBatchAsync(request);

        // проверка, что в FetchAsync ушла 1
        _storageMock.Verify(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<string>(), 
                It.IsAny<string>(), 1, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), 
            Times.Once);
    }

    [Fact] // тест на валидацию (пустой MessageId)
    public async Task AcknowledgeAsync_WhenMessageIdIsEmpty_ShouldReturnFalse()
    {
        var request = new AcknowledgeRequest { MessageId = "", ConsumerId = "c1" };
        
        var result = await _service.AcknowledgeAsync(request);

        Assert.False(result.Acknowledged);
        
        // проверка, что MessageId в ответе пустой 
        Assert.Equal("", result.MessageId);
    }

    [Fact] // тест на успешный запрос
    public async Task AcknowledgeAsync_WhenRequestIsValid_ShouldReturnTrue()
    {
        var request = new AcknowledgeRequest { MessageId = "m1", ConsumerId = "c1" };
        
        _storageMock.Setup(s => s.TryAcknowledgeAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        
        var result = await _service.AcknowledgeAsync(request);
        
        Assert.True(result.Acknowledged);
        
        _storageMock.Verify(s => s.TryAcknowledgeAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact] // выдаст ли хранилище, что сообщение не найдено
    public async Task AcknowledgeAsync_WhenMessageNotFound_ShouldReturnFalse()
    {
        var request = new AcknowledgeRequest { MessageId = "m1", ConsumerId = "c1" };
        
        _storageMock.Setup(s => s.TryAcknowledgeAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); 
        
        var result = await _service.AcknowledgeAsync(request);

        Assert.False(result.Acknowledged);
        Assert.Equal(result.MessageId, request.MessageId);
        
        _storageMock.Verify(s => s.TryAcknowledgeAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectRequest_WhenUnnessasaryMessage_ShouldSendToDlq()
    {
        var request = new RejectRequest { MessageId = "bad-m", ConsumerId = "c1", Requeue = false};
        
        _storageMock.Setup(s => s.TryRejectAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        
        var result = await _service.RejectAsync(request);

        Assert.True(result.Rejected);
        Assert.True(result.MovedToDlq);
        
        _storageMock.Verify(s => s.TryRejectAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    } 
}
