using Client.Configuration;
using Client.Connection;
using Client.Consumer;
using Client.Producer;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBrokerClient(this IServiceCollection services, Action<BrokerOptions> configure)
    {
        var options = new BrokerOptions();

        // пользователь заполняет поля
        configure(options);

        // валидация
        if (string.IsNullOrWhiteSpace(options.Address))
        {
            throw new ArgumentException("Адрес обязателен для заполнения");
        }

        // регистрация настроек
        services.AddSingleton(options);

        // регистрация соединения
        services.AddSingleton<IBrokerConnection>(_ => new BrokerConnection(options));

        services.AddScoped<IMessagePublisher, MessagePublisher>();
        services.AddScoped<IMessageConsumer, MessageConsumer>();

        return services;
    }
}