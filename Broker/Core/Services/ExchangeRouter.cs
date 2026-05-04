using Broker.Contracts;
using Core.Abstractions;

namespace Core.Services;

public sealed class ExchangeRouter(IMessageStorage messageStorage) : IExchangeRouter
{
    public async Task<IReadOnlyCollection<string>> RouteAsync(
        Message message,
        CancellationToken ct = default)
    {
        var routingKey = message.Queue;

        if (string.IsNullOrWhiteSpace(routingKey))
            return [];

        var queueInfos = await messageStorage.ListQueuesAsync(new ListQueuesRequest(), ct);
        
        var queueNames = queueInfos
            .Queues
            .Select(x => x.Name)
            .ToArray();

        var matchedQueues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var queueName in queueNames)
        {
            if (IsMatch(routingKey, queueName))
            {
                matchedQueues.Add(queueName);
            }
        }

        return matchedQueues;
    }

    private static bool IsMatch(string routingKey, string bindingKey)
    {
        if (string.Equals(bindingKey, routingKey, StringComparison.OrdinalIgnoreCase))
            return true;

        return MatchTopic(routingKey, bindingKey);
    }

    private static bool MatchTopic(string pattern, string routingKey)
    {
        var p = pattern.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var k = routingKey.Split('.', StringSplitOptions.RemoveEmptyEntries);

        return Match(0, 0);

        bool Match(int i, int j)
        {
            while (i < p.Length)
            {
                if (p[i] == "#")
                {
                    if (i == p.Length - 1)
                        return true;

                    for (var x = j; x <= k.Length; x++)
                    {
                        if (Match(i + 1, x))
                            return true;
                    }

                    return false;
                }

                if (j >= k.Length)
                    return false;

                if (p[i] != "*" && !string.Equals(p[i], k[j], StringComparison.OrdinalIgnoreCase))
                    return false;

                i++;
                j++;
            }

            return j == k.Length;
        }
    }
}