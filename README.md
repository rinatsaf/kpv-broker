# Broker: Client Library & Server Guide

Система обмена сообщениями на базе gRPC с поддержкой очередей, групп потребителей и мониторинга.


## 1. Быстрый старт (Server)

### Системные требования
* .NET 8.0 SDK
* Docker (рекомендуется)
* Хранилище (по умолчанию: In-Memory / PostgreSQL)

### Запуск через Docker
```
# Сборка образа
docker build -t message-broker -f Broker.Service/Dockerfile .

# Запуск
docker run -d -p 5000:5000 -p 5001:5001 --name my-broker message-broker
```
* **Порт 5000**: gRPC (Publisher, Consumer, Queue Services).
* **Порт 5001**: Admin API / Metrics.

## 2. Установка Client Library (NuGet)

Библиотека `Broker.Client` инкапсулирует работу с gRPC и предоставляет удобные интерфейсы для интеграции в ваше приложение.

```
dotnet add package Broker.Client
```

## 3. Гайд для Producer (Публикация)

Используйте `IMessagePublisher` для отправки данных. Сообщения поддерживают произвольные заголовки и бинарную нагрузку (`payload`).

### Пример отправки сообщения:
```
// Инициализация подключения
using var connection = new BrokerConnection("localhost:5000");
var publisher = new MessagePublisher(connection);

// 1. Простая отправка
var payload = Encoding.UTF8.GetBytes("Hello World");
var response = await publisher.PublishAsync("orders-queue", payload);

// 2. Отправка с заголовками
var headers = new Dictionary<string, string> { { "priority", "high" } };
await publisher.PublishAsync("orders-queue", payload, headers);

// 3. Пакетная отправка (оптимально для высокой нагрузки)
var batch = new List<byte[]> { /* ... несколько payloads ... */ };
await publisher.PublishBatchAsync("orders-queue", batch);
```

## 4. Гайд для Consumer (Подписка)

Поддерживается два режима работы: **Streaming** (реальное время) и **Batch** (обработка пачками).

### А) Режим Streaming (рекомендуется)
Использует `IAsyncEnumerable` для получения сообщений сразу после их появления в очереди.

```
var consumer = new MessageConsumer(connection);
var cts = new CancellationTokenSource();

await foreach (var @event in consumer.SubscribeAsync("orders-queue", "billing-group", "consumer-1", cts.Token))
{
    var msg = @event.Message;
    try 
    {
        Process(msg.Payload);
        
        // Подтверждение получения
        await consumer.AckAsync(msg.Id, "consumer-1");
    }
    catch (Exception) 
    {
        // Отклонение (сообщение может попасть в DLQ)
        await consumer.RejectAsync(msg.Id, "consumer-1");
    }
}
```

### Б) Режим Batch
Полезно для сценариев, где нужно вычитывать сообщения по расписанию или большими порциями.
```
var messages = await consumer.ConsumeBatchAsync("orders-queue", "billing-group", "consumer-1", maxMessages: 100, ct);
```

---

## 5. Архитектура сообщения (Proto)

Структура `Message`, которую вы получаете и отправляете:

| Поле | Тип | Описание |
| :--- | :--- | :--- |
| `id` | `string` | GUID сообщения (генерируется клиентом или сервером). |
| `queue` | `string` | Целевая очередь. |
| `payload` | `bytes` | Тело сообщения в бинарном формате. |
| `headers` | `map` | Словарь пользовательских метаданных. |
| `delivery_count` | `int32` | Счетчик попыток доставки (увеличивается сервером). |


## 6. Администрирование и мониторинг

Брокер предоставляет `QueueService` и `MonitoringService` для управления инфраструктурой:

* **DLQ (Dead Letter Queue)**: Если сообщение превышает `max_delivery_attempts`, оно автоматически перемещается в DLQ (если настроено).
* **Управление очередями**: Создание очередей с лимитами (`max_size`, `ttl_seconds`) через gRPC клиент.
* **Метрики**: Доступ к `GetBrokerStatus` для получения информации о количестве активных соединений и нагрузке на очереди.


### Рекомендации по использованию
1.  **Consumer Groups**: Используйте одно и то же имя `consumer_group` для нескольких экземпляров одного сервиса, чтобы распределить нагрузку между ними (Round-robin внутри группы).
2.  **Idempotency**: Всегда проверяйте `message.id` на стороне потребителя, чтобы избежать дублирующей обработки в случае сетевых сбоев.
3.  **Cancellation**: Всегда передавайте `CancellationToken` в `SubscribeAsync`, чтобы корректно закрывать gRPC-стрим при остановке приложения.

