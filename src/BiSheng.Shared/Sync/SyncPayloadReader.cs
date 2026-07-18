using System;
using System.Text.Json;

namespace BiSheng.Shared.Sync;

/// <summary>从同步 Payload JSON 读取字段（兼容 camelCase / PascalCase）</summary>
public static class SyncPayloadReader
{
    public static bool TryGetPropertyIgnoreCase(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.TryGetProperty(propertyName, out value))
            return true;

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public static string ReadString(JsonElement root, string propertyName, string currentValue = "")
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out var element))
            return currentValue;

        return element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? currentValue
            : currentValue;
    }

    public static Guid ReadGuid(JsonElement root, string propertyName, Guid currentValue = default)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out var element))
            return currentValue;

        if (element.ValueKind == JsonValueKind.String
            && Guid.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }

        if (element.TryGetGuid(out var guid))
            return guid;

        return currentValue;
    }

    public static Guid? ReadNullableGuid(JsonElement root, string propertyName, Guid? currentValue = null)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out var element))
            return currentValue;

        if (element.ValueKind == JsonValueKind.Null)
            return null;

        if (element.ValueKind == JsonValueKind.String
            && Guid.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }

        if (element.TryGetGuid(out var guid))
            return guid;

        return currentValue;
    }

    public static bool ReadBool(JsonElement root, string propertyName, bool currentValue = false)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out var element))
            return currentValue;

        if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
            return element.GetBoolean();

        return currentValue;
    }
}
