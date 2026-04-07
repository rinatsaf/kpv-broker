var builder = WebApplication.CreateBuilder(args);

// добавление gRPC в сервисы
builder.Services.AddGrpc();

var app = builder.Build();

// регистрация сервиса 
app.MapGrpcService<Core.Services.PublisherService>(); 

app.Run();