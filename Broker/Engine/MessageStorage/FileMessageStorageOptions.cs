using System.Text.Json;
using System.Text.Json.Serialization;

namespace Engine.MessageStorage;

public sealed class FileMessageStorageOptions
{
    public string RootPath { get; set; } = string.Empty;

    public JsonSerializerOptions JsonOptions { get; set; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };
}
