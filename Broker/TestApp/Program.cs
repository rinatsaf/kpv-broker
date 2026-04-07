using Client.Extensions;
using Client.Producer;
using Microsoft.Extensions.DependencyInjection;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var services = new ServiceCollection();

services.AddBrokerClient("http://localhost:5113"); 

var serviceProvider = services.BuildServiceProvider();

// паблишер из контейнера
var publisher = serviceProvider.GetRequiredService<IMessagePublisher>();

Console.WriteLine("Начинаем отправку сообщения...");

try 
{
    // тестовое сообщение
    var response = await publisher.PublishAsync(
        queue: "test-queue", 
        payload: System.Text.Encoding.UTF8.GetBytes("Hello, Message Bus!"),
        headers: new Dictionary<string, string> { { "Priority", "High" } }
    );

    if (response.Accepted)
    {
        Console.WriteLine($"Успех! Сообщение принято брокером. ID: {response.MessageId}");
    }
    else
    {
        Console.WriteLine("Брокер отклонил сообщение.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Ошибка подключения: {ex.Message}");
}