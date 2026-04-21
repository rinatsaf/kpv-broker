namespace Client.Configuration;

public class BrokerOptions
{
    // адрес сервера 
    public string Address { get; set; } = string.Empty;

    // имя приложения
    public string ClientName { get; set; } = "DefaultClient";

    // идентификатор конкретного экземпляра 
    public string ClientId { get; set; } = Guid.NewGuid().ToString("N");
}