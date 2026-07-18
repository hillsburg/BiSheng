using BiSheng.Shared.Sync;

namespace BiSheng.Server.Services.Mutations;

/// <summary>Sync DTO 与内部变更模型的映射</summary>
public static class ClientChangeDtoExtensions
{
    /// <summary>将 Push 变更转为 Writer 使用的 EntityMutation</summary>
    public static EntityMutation ToEntityMutation(this ClientChangeDto change) => new()
    {
        EntityType = change.EntityType,
        EntityId = change.EntityId,
        Action = change.Action,
        Payload = change.Payload,
        UpdatedAt = change.UpdatedAt
    };
}
