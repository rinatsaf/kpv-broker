using Api.Connection;
using Api.MonitoringClient;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IBrokerConnection>(new BrokerConnection("http://localhost:5113"));
builder.Services.AddSingleton<MonitoringClient>();

var app = builder.Build();

app.UseHttpsRedirection();

app.MapGet("/metrics/{queueName}",  async (MonitoringClient client, string queueName) =>
{
    var ctx = new CancellationTokenSource();
    var m = await client.GetMetricsAsync(queueName, 0, long.MaxValue,  ctx.Token);
    return Results.Ok(m);
});

app.MapGet("/status",  async (MonitoringClient client) =>
{
    var ctx = new CancellationTokenSource();
    var m = await client.GetBrokerStatus(ctx.Token);
    return Results.Ok(m);
});

app.Run();