using Core.Abstractions;
using Core.Services;
using Engine.Endpoints;
using Engine.MessageStorage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

var storagePath = Path.Combine(builder.Environment.ContentRootPath, "data");
builder.Services.AddSingleton<IMessageStorage>(_ => new FileMessageStorage(storagePath));

builder.Services.AddSingleton<IPublisherService, PublisherService>();
builder.Services.AddSingleton<IConsumerService, ConsumerService>();
builder.Services.AddSingleton<IQueueManagementService, QueueManagementService>();
builder.Services.AddSingleton<IMonitoringService, MonitoringService>();
builder.Services.AddSingleton<IConfigService, ConfigService>();

builder.Services.AddHostedService<StorageMaintenanceService>();

var app = builder.Build();

app.MapGrpcService<PublisherGrpcEndpoint>();
app.MapGrpcService<ConsumerGrpcEndpoint>();
app.MapGrpcService<QueueGrpcEndpoint>();
app.MapGrpcService<MonitoringGrpcEndpoint>();
app.MapGrpcService<ConfigGrpcEndpoint>();

app.Run();
