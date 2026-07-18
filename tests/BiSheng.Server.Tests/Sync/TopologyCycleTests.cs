using BiSheng.Server.Services;
using BiSheng.Server.Tests.Fixtures;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Tests.Sync;

/// <summary>
/// M：文件夹父子关系成环时，Push 应显式检测并记入失败，不静默丢弃
/// </summary>
public class TopologyCycleTests
{
    /// <summary>
    /// 两个 folder 互为父（A.parent=B, B.parent=A），Push 后两者都在 FailedEntityIds，
    /// 都未落库，错误信息提及"环"
    /// </summary>
    [Fact]
    public async Task Push_CyclicFolderParent_ReturnsBothInFailedEntityIds()
    {
        using var fixture = new TestDbFactory();
        var (userId, apiKeyId, _, _) = fixture.SeedUserWithNote();
        var sync = SyncServiceFactory.New(fixture.Db);

        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();

        var resp = await sync.PushAsync(userId, apiKeyId, new SyncPushRequest
        {
            ClientVersion = 10,
            Changes = new()
            {
                new ClientChangeDto
                {
                    EntityType = EntityTypes.Folder,
                    EntityId = aId,
                    Action = ChangeActions.Create,
                    Payload = $$"""{"name":"A","parentId":"{{bId}}","isFavorite":false,"isPinned":false}"""
                },
                new ClientChangeDto
                {
                    EntityType = EntityTypes.Folder,
                    EntityId = bId,
                    Action = ChangeActions.Create,
                    Payload = $$"""{"name":"B","parentId":"{{aId}}","isFavorite":false,"isPinned":false}"""
                }
            }
        }, CancellationToken.None);

        // 两者都记入失败
        Assert.False(resp.Success);
        Assert.Contains(aId, resp.FailedEntityIds);
        Assert.Contains(bId, resp.FailedEntityIds);

        // 错误信息显式提到环
        Assert.Contains(resp.Errors, e => e.Contains("环"));

        // 两者都未落库
        Assert.Null(await fixture.Db.Folders.FindAsync(aId));
        Assert.Null(await fixture.Db.Folders.FindAsync(bId));
    }
}
