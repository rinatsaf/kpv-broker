using Broker.Contracts;

namespace Core.Abstractions;

public interface IConfigService
{
    Task<GetConfigResponse> GetConfigAsync(
        GetConfigRequest request,
        CancellationToken ct = default);

    Task<UpdateConfigResponse> UpdateConfigAsync(
        UpdateConfigRequest request,
        CancellationToken ct = default);
}