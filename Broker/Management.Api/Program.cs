using Api.Connection;
using Api.MonitoringClient;
using Broker.Contracts;
using Management.Api.MonitoringClient;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton<IBrokerConnection>(new BrokerConnection("http://localhost:5113"));
builder.Services.AddSingleton<MonitoringClient>();
builder.Services.AddSingleton<QueueClient>();
builder.Services.AddSingleton<ConfigClient>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<Management.Api.Components.App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/", () => Results.Redirect("/dashboard"));

app.MapGet("/api/metrics/{queueName}",  async (MonitoringClient client, string queueName) =>
{
    var ctx = new CancellationTokenSource();
    var m = await client.GetMetricsAsync(queueName, 0, long.MaxValue, ctx.Token);
    return Results.Ok(m);
});

app.MapGet("/api/status",  async (MonitoringClient client) =>
{
    var ctx = new CancellationTokenSource();
    var m = await client.GetBrokerStatus(ctx.Token);
    return Results.Ok(m);
});

app.MapGet("/api/list-queues",  async ([FromServices] QueueClient client) =>
{
    var ctx = new CancellationTokenSource();
    var m = await client.ListQueues(ctx.Token);
    return Results.Ok(m);
});

app.MapGet("/api/config",  async ([FromServices] ConfigClient client) =>
{
    var ctx = new CancellationTokenSource();
    var m = await client.GetConfigAsync(ctx.Token);
    return Results.Ok(m);
});

app.MapPost("/api/queues",  async ([FromServices] QueueClient client, [FromBody] CreateQueueRequest request) =>
{
    var ctx = new CancellationTokenSource();
    var m = await client.CreateQueue(request, ctx.Token);
    return Results.Ok(m);
});

app.MapDelete("/api/queues/{name}",  async ([FromServices] QueueClient client, string name) =>
{
    var ctx = new CancellationTokenSource();
    var m = await client.DeleteQueue(new DeleteQueueRequest { Name = name }, ctx.Token);
    return Results.Ok(m);
});

app.MapGet("/api/queues/{name}",  async ([FromServices] QueueClient client, string name) =>
{
    var ctx = new CancellationTokenSource();
    var m = await client.GetQueueInfo(new GetQueueInfoRequest { Name = name }, ctx.Token);
    return Results.Ok(m);
});

app.MapPost("/api/queues/{name}/purge",  async ([FromServices] QueueClient client, string name) =>
{
    var ctx = new CancellationTokenSource();
    var m = await client.PurgeQueue(new PurgeQueueRequest { Name = name }, ctx.Token);
    return Results.Ok(m);
});

app.MapPut("/api/config",  async ([FromServices] ConfigClient client, [FromBody] UpdateConfigRequest request) =>
{
    var ctx = new CancellationTokenSource();
    var m = await client.UpdateConfigAsync(request, ctx.Token);
    return Results.Ok(m);
});

app.Run();
