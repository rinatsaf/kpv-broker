using Client.Consumer;
using Client.Extensions;
using Client.Producer;
using Microsoft.Extensions.DependencyInjection;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var services = new ServiceCollection();

services.AddBrokerClient(options => 
{
    options.Address = "http://localhost:5000";
    options.ClientName = "Test";
});

var serviceProvider = services.BuildServiceProvider();

var cts = new CancellationTokenSource();

Console.WriteLine("Подписываемся на новые сообщения...");

var subscriberTask = Task.Run(async () =>
{
    try
    {
        var subscriber = serviceProvider.GetRequiredService<IMessageConsumer>();

        var messages = subscriber.SubscribeAsync(
            queue: "test-queue",
            consumerGroup: "test-group",
            consumerId: "very-unique-id",
            ct: cts.Token
        );

        await foreach (var message in messages)
        {
            Console.WriteLine($"[Sub] Успех! Брокер вернул сообщение. ID: {message.Message.Id}");
        }
        Console.WriteLine($"[Sub] Брокер закончил отправлять сообщения");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Sub] Ошибка: {ex.Message}");
    }
});

Console.WriteLine("Начинаем отправку сообщения...");

var publishTask = Task.Run(async () =>
{
    try
    {
        // паблишер из контейнера
        var publisher = serviceProvider.GetRequiredService<IMessagePublisher>();

        // тестовое сообщение
        var response = await publisher.PublishAsync(
            queue: "test-queue",
            payload: System.Text.Encoding.UTF8.GetBytes("Hello, Message Bus!"),
            headers: new Dictionary<string, string> { { "Priority", "High" } }
        );

        if (response.Accepted)
        {
            Console.WriteLine($"[Pub] Успех! Сообщение принято брокером. ID: {response.MessageId}");
        }
        else
        {
            Console.WriteLine("[Pub] Брокер отклонил сообщение.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Pub] Ошибка: {ex.Message}");
    }
});

await Task.WhenAll(publishTask, subscriberTask);