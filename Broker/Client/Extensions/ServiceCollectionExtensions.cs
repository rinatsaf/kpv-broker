using Client.Connection;
using Client.Consumer;
using Client.Producer;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBrokerClient(this IServiceCollection services, string address)
    {
        services.AddSingleton<IBrokerConnection>(_ => new BrokerConnection(address));
        services.AddScoped<IMessagePublisher, MessagePublisher>();
        services.AddScoped<IMessageConsumer, MessageConsumer>();
        
        return services;
    }
}