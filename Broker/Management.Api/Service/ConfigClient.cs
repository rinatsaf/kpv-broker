using Api.Connection;
using Broker.Contracts;

namespace Management.Api.MonitoringClient;

public class ConfigClient(IBrokerConnection connection)
{
    private readonly ConfigService.ConfigServiceClient _client = connection.GetConfigClient();

    public async Task<BrokerConfig> GetConfigAsync(CancellationToken ct)
    {
        var res = await _client.GetConfigAsync(new GetConfigRequest {}, cancellationToken: ct);
        return res.Config;
    }

    public async Task<UpdateConfigResponse> UpdateConfigAsync(UpdateConfigRequest request, CancellationToken ct)
    {
        return await _client.UpdateConfigAsync(request, cancellationToken: ct);
    }
}