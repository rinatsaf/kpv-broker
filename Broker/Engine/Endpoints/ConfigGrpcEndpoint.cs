using Broker.Contracts;
using Core.Abstractions;
using Grpc.Core;

namespace Engine.Endpoints;

public sealed class ConfigGrpcEndpoint(IConfigService service) : ConfigService.ConfigServiceBase
{
    public override Task<GetConfigResponse> GetConfig(GetConfigRequest request, ServerCallContext context) =>
        service.GetConfigAsync(request, context.CancellationToken);

    public override Task<UpdateConfigResponse> UpdateConfig(UpdateConfigRequest request, ServerCallContext context) =>
        service.UpdateConfigAsync(request, context.CancellationToken);
}