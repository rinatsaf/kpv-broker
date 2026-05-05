
using Broker.Contracts;
using Core.Abstractions;

namespace Core.Services;

public class ConfigService : IConfigService
{
    public Task<GetConfigResponse> GetConfigAsync(GetConfigRequest request, CancellationToken ct = default)
    {
        return Task.FromResult(new GetConfigResponse
        {
            
        });
    }

    public Task<UpdateConfigResponse> UpdateConfigAsync(UpdateConfigRequest request, CancellationToken ct = default)
    {
        return Task.FromResult(new UpdateConfigResponse
        {
            
        });
    }
}