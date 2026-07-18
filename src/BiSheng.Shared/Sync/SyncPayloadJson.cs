using System.Text.Json;

namespace BiSheng.Shared.Sync;

/// <summary>同步 Payload 统一 JSON 选项（camelCase）</summary>
public static class SyncPayloadJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
