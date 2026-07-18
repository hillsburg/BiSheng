using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BiSheng.Shared.Sync;

/// <summary>
/// 同步 Payload 语义指纹：先解析 JSON 再对业务字段做 SHA-256，用于等价比较。
/// 不可直接对原始 JSON 字符串做摘要（键序、空白会导致误判）。
/// </summary>
public static class SyncPayloadFingerprint
{
    /// <summary>两段 Payload 在业务语义上是否等价</summary>
    public static bool AreEquivalent(string entityType, string? localPayloadJson, string? remotePayloadJson)
    {
        if (string.IsNullOrEmpty(localPayloadJson) || string.IsNullOrEmpty(remotePayloadJson))
        {
            return string.Equals(localPayloadJson, remotePayloadJson, StringComparison.Ordinal);
        }

        var local = Compute(entityType, localPayloadJson!);
        var remote = Compute(entityType, remotePayloadJson!);

        if (local == null || remote == null)
        {
            return false;
        }

        return local == remote;
    }

    /// <summary>计算 Payload 语义指纹；解析失败返回 null</summary>
    public static string? Compute(string entityType, string payloadJson)
    {
        try
        {
            if (entityType == EntityTypes.Note)
            {
                var payload = JsonSerializer.Deserialize<NoteChangePayload>(payloadJson, SyncPayloadJson.Options);
                if (payload == null)
                {
                    return null;
                }

                return HashNote(payload);
            }

            if (entityType == EntityTypes.Folder)
            {
                var payload = JsonSerializer.Deserialize<FolderChangePayload>(payloadJson, SyncPayloadJson.Options);
                if (payload == null)
                {
                    return null;
                }

                return HashFolder(payload);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string HashNote(NoteChangePayload payload)
    {
        var canonical = $"{payload.Title}\0{payload.Content ?? string.Empty}\0{payload.FolderId:D}\0{(payload.IsFavorite ? 1 : 0)}\0{(payload.IsPinned ? 1 : 0)}";
        return Sha256Hex(canonical);
    }

    private static string HashFolder(FolderChangePayload payload)
    {
        var parent = payload.ParentId?.ToString("D") ?? string.Empty;
        var canonical = $"{payload.Name}\0{parent}\0{(payload.IsFavorite ? 1 : 0)}\0{(payload.IsPinned ? 1 : 0)}";
        return Sha256Hex(canonical);
    }

    private static string Sha256Hex(string text)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        return BitConverter.ToString(bytes).Replace("-", string.Empty);
    }
}
