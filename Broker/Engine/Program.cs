using Core.Abstractions;
using Core.Services;
using Engine.Endpoints;
using Engine.MessageStorage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

var storagePath = Path.Combine(builder.Environment.ContentRootPath, "data");
builder.Services.Configure<FileMessageStorageOptions>(opts => opts.RootPath = storagePath);
builder.Services.AddSingleton<IMessageStorage, FileMessageStorage>();

builder.Services.AddSingleton<IPublisherService, PublisherService>();
builder.Services.AddSingleton<IConsumerService, ConsumerService>();
builder.Services.AddSingleton<IQueueManagementService, QueueManagementService>();
builder.Services.AddSingleton<IMonitoringService, MonitoringService>();

builder.Services.AddHostedService<StorageMaintenanceService>();

var app = builder.Build();

app.MapGrpcService<PublisherGrpcEndpoint>();
app.MapGrpcService<ConsumerGrpcEndpoint>();
app.MapGrpcService<QueueGrpcEndpoint>();
app.MapGrpcService<MonitoringGrpcEndpoint>();

app.Run();
