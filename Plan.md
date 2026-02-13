# Проект: Собственный Message Broker (RabbitMQ-like) на .NET 9
**Состав:**  
1) **Broker.Engine** — отдельное .NET приложение (не Web API)  
2) **Broker.Client (SDK)** — библиотека для микросервисов (в основном ASP.NET)  
3) **Broker.Management.Api** — ASP.NET Core Web API (Controllers) для наблюдения/управления  
4) (Опционально) **Web UI** — браузерная панель (Rabbit UI-lite)

---

## 1. Почему так разделяем на части
### Зачем отдельный Broker.Engine
- Брокер — это **инфраструктурный процесс**, который должен быть:
  - быстрым (data-plane),
  - стабильным,
  - минимально зависимым от веб-слоя.
- Вынесение в отдельное приложение позволяет:
  - деплоить/масштабировать брокер отдельно от API/UI,
  - не тянуть лишнюю “вебовую” нагрузку на поток обработки сообщений,
  - упрощать отказоустойчивость (упал UI — брокер продолжает работать).

### Зачем SDK
- Микросервисы не должны знать детали транспорта, сериализации, retry, reconnect.
- SDK даёт единый удобный API и общие практики (correlationId, timeouts).

### Зачем отдельный Management API
- Management — это **control-plane** (наблюдение и управление), не “горячий путь”.
- Через API можно дать пользователю:
  - прозрачность (что происходит),
  - диагностику (где узкое место),
  - управление (purge, pause, dlq, stats).

---

## 2. Компонент 1: Broker.Engine (ядро брокера как отдельное .NET приложение)

### 2.1 Назначение
Broker.Engine — основной процесс брокера:
- принимает сообщения от продюсеров;
- хранит их в очередях (in-memory на v0.1);
- раздаёт сообщения консьюмерам;
- обрабатывает ACK/NACK;
- делает redelivery и DLQ.

> Broker.Engine НЕ предоставляет человеку веб-страницы и НЕ является Web API.  
> Он выполняет высоконагруженную работу доставки.

---

### 2.2 Почему используем gRPC/TCP в ядре (data-plane)
Взаимодействие микросервисов с брокером — это **частые операции** (publish/consume/ack). Для этого важны: низкая задержка, высокая пропускная способность, стабильность соединений.

#### Почему gRPC (а не HTTP JSON)
- **Бинарная сериализация (protobuf)** — меньше размер сообщений и CPU overhead.
- **Строгий контракт** (schema) — меньше ошибок интеграции, проще версионировать.
- **Streaming** — идеален для `Consume`: брокер может пушить сообщения в одном долгоживущем соединении.
- **Генерация клиента** — SDK проще и надёжнее (не писать вручную HTTP клиенты/DTO).

#### Почему TCP (и долгоживущие соединения)
- Для доставки сообщений выгодно держать **постоянное соединение**, а не создавать новое на каждый запрос:
  - меньше накладных расходов на handshake;
  - стабильнее latency;
  - проще реализовать backpressure/prefetch;
  - лучше для высоких нагрузок.
- gRPC в .NET работает поверх **HTTP/2**, который использует **TCP** и поддерживает multiplexing (несколько запросов в одном соединении).

#### Почему это важно именно “в ядре”
Data-plane должен быть:
- максимально быстрым,
- минимально “болтливым” (меньше заголовков и JSON),
- с постоянными каналами и контролем потока.

> Итог: gRPC/HTTP2(TCP) — это практичный выбор для “быстрого брокера”, особенно для streaming-консьюмеров.

---

### 2.3 Внутренняя реализация (v0.1)
**Broker.Core (библиотека логики)** используется внутри Engine.

#### Основные модули
- **QueueRegistry**: хранит очереди по имени
- **QueueEngine**:
  - Ready queue (готовые сообщения)
  - Выдача сообщений консьюмерам
- **ConsumerManager**:
  - активные consumers
  - prefetch лимиты
- **InflightStore**:
  - messageId -> consumerId, deadline, attempts
- **RedeliveryWorker** (BackgroundService):
  - проверяет таймауты inflight
  - requeue и attempts++
- **DLQManager**:
  - перенос сообщений в DLQ после N попыток или NACK(requeue=false)

#### Гарантия доставки
- **At-least-once**:
  - сообщение может прийти повторно (при таймауте, reconnect и т.п.)
  - обработчики должны быть идемпотентными (при необходимости)

---

### 2.4 Базовые операции Broker.Engine (gRPC)
- `Produce(queue, payload, headers, messageId, correlationId)`
- `Consume(queue, consumerId, prefetch) -> stream Delivery`
- `Ack(messageId)`
- `Nack(messageId, requeue)`
- (опционально) `Ping/Heartbeat` (если нужно наблюдение за соединениями)

---

## 3. Компонент 2: Broker.Client (SDK библиотека)

### 3.1 Назначение
SDK упрощает использование брокера в приложениях (в основном ASP.NET сервисах):
- publish без “ручного” gRPC кода;
- subscribe как удобный handler;
- auto-ack/auto-nack;
- reconnect/timeout/retry publish.

### 3.2 Почему SDK именно библиотека, а не “копипаста”
- единые настройки (timeouts, prefetch, naming);
- единый формат headers/correlation;
- единая обработка ошибок;
- проще обновлять протокол и сохранять совместимость.

### 3.3 Что должен уметь SDK (v0.1)
- **ProducerClient**
  - `PublishAsync(queue, payload, headers?)`
- **ConsumerClient**
  - `Subscribe(queue, handler, options)`
  - handler success -> Ack
  - handler exception -> Nack(requeue=true) по политике
- интеграция с DI:
  - `services.AddBrokerClient(...)`

---

## 4. Компонент 3: Broker.Management.Api (ASP.NET Web API, Controllers)

### 4.1 Главная цель Management: наблюдаемость и управление
Management API должен дать пользователю (человеку/оператору) возможность:
- **видеть** состояние брокера (очереди, consumers, скорость обработки);
- **диагностировать** проблемы (рост очереди, зависшие unacked, DLQ);
- **управлять** базовыми операциями (purge, declare/delete queue, просмотр DLQ).

> Важно: Management — не должен вмешиваться в data-plane как основной путь доставки.  
> Он читает состояние и выполняет безопасные команды управления.

---

### 4.2 Что именно нужно пользователю для “отслеживания работы брокера”
Ниже список функций, которые покрывают типичные кейсы Rabbit UI.

#### A) Мониторинг очередей
- список очередей:
  - `ready` (сообщения в очереди)
  - `unacked` (inflight)
  - `consumers` count
  - `publish/deliver/ack rate` (msg/s)
- детали очереди:
  - график или значения за последние N секунд
  - maxAttempts, redeliveryTimeout, prefetch defaults
  - размер DLQ (если есть)

**Почему важно:** оператор сразу видит “где пробка”: ready растёт? unacked растёт? consumers=0?

#### B) Мониторинг консьюмеров
- список consumers/подключений:
  - consumerId
  - queue name
  - prefetch
  - inflight count
  - lastSeen/connectedSince
  - скорость ack

**Почему важно:** понять “кто тормозит”, кто отвалился, кто держит unacked.

#### C) DLQ (ошибки)
- просмотр сообщений в DLQ:
  - messageId, attempts, lastError (если сохраняем), время
- действия:
  - requeue обратно
  - удалить/ack-delete (по решению)

**Почему важно:** DLQ — главный инструмент эксплуатации: “почему не обработалось” и “что делать дальше”.

#### D) Администрирование очередей (безопасный минимум)
- declare queue
- delete queue
- purge queue (удалить все сообщения)
- peek messages (посмотреть N первых без удаления)

**Почему важно:** управление жизненным циклом очередей + диагностика.

---

### 4.3 Как Management получает данные
Два варианта (выбираем один на проекте):

**Вариант 1 (рекомендуется): internal gRPC Admin API**
- Broker.Engine дополнительно предоставляет `Admin gRPC` методы:
  - `GetQueuesStats`
  - `GetConsumers`
  - `GetQueueDetails`
  - `GetDlqMessages`
  - `PurgeQueue`, `DeclareQueue`, ...
- Management API вызывает эти методы.

Плюсы:
- Management не лезет в память брокера напрямую
- проще разделить на разные процессы/контейнеры
- безопаснее и масштабируемее

**Вариант 2: общая библиотека состояния (если в одном процессе)**
- Management живёт в одном процессе и читает registry напрямую (не рекомендуется на долгую)

---

### 4.4 Endpoints Management API (Controllers)
Пример v0.1:

**QueuesController**
- `GET /api/v1/queues`
- `GET /api/v1/queues/{name}`
- `POST /api/v1/queues` (declare)
- `DELETE /api/v1/queues/{name}`
- `POST /api/v1/queues/{name}/purge`
- `GET /api/v1/queues/{name}/messages/peek?count=N`

**ConsumersController**
- `GET /api/v1/consumers`

**DlqController**
- `GET /api/v1/dlq/{queueName}`
- `POST /api/v1/dlq/{queueName}/requeue` (optional)

---

## 5. (Опционально) Web UI
Web UI — это “лицо” для человека, чтобы:
- видеть списки очередей и цифры
- смотреть DLQ
- выполнять purge/declare/requeue

UI общается ТОЛЬКО с Management API.

---

## 6. Структура Solution (как понимать какие проекты создавать)
/src
Broker.Engine
- .NET 9 Console Application
- gRPC сервер (data-plane + optional admin-plane)
- Хостит Broker.Core

Broker.Core
- Чистая бизнес-логика брокера
- Очереди, inflight, retry, DLQ
- НЕ зависит от ASP.NET и gRPC

Broker.Contracts
- protobuf файлы
- Общие модели сообщений (Envelope, DTO)
- Используется Broker.Engine и Broker.Client

Broker.Client
- SDK для микросервисов
- Publish / Subscribe API
- DI extensions
- Использует Grpc.Net.Client

Broker.Management.Api
- ASP.NET Core Web API (Controllers)
- Администрирование и мониторинг брокера
- Swagger
- gRPC клиент к Broker.Engine (admin методы)

Broker.Management.Ui (опционально)
- Web UI (React / Blazor)
- Работает через Management API

/tests
Broker.Core.Tests
Broker.Engine.IntegrationTests
Broker.Client.Tests

/samples
Sample.Producer
Sample.Consumer
Demo.OrdersService
Demo.BillingService

/infra
docker-compose.yml
docker/
---

## 7. Технологический стек по компонентам (коротко)

### Broker.Engine (Console)
- .NET 9, C# 13
- Generic Host
- gRPC server (HTTP/2)
- Channels + BackgroundService
- Serilog

### Broker.Client (SDK)
- .NET 9 class library
- Grpc.Net.Client
- DI + Options

### Broker.Management.Api (Controllers)
- ASP.NET Core Web API
- Controllers + Swagger
- auth (API key/JWT)
- internal gRPC client к Broker.Engine

---

## 8. Что считается “готовым” для пользователя (минимум v0.1)
- Broker.Engine работает как отдельный процесс
- SDK позволяет:
  - Publish
  - Subscribe
  - Ack/Nack автоматически
- Брокер поддерживает:
  - prefetch
  - inflight
  - redelivery timeout
  - DLQ по attempts
- Management API позволяет пользователю:
  - видеть очереди (ready/unacked/rates)
  - видеть consumers
  - смотреть DLQ
  - выполнять declare/purge/peek

---
