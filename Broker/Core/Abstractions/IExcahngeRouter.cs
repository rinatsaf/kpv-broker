namespace Core.Abstractions;

using Broker.Contracts;

public interface IExchangeRouter
{
    Task<IReadOnlyCollection<string>> RouteAsync(
        Message message,
        CancellationToken ct = default);
}