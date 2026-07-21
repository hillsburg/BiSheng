using BiSheng.Latte.Models;
using BiSheng.Latte.Services;

namespace BiSheng.Latte.Tests.Services;

/// <summary>连接徽章：有待推送时不得显示「已同步」</summary>
public class ConnectionDisplayResolverTests
{
    private static AuthService CreateVerifiedAuth()
    {
        var auth = new AuthService
        {
            ServerUrl = "http://127.0.0.1:8090",
            ApiKey = "test-key",
            IsSyncEnabled = true,
            Username = "u"
        };
        auth.SetServerVerified(true);
        return auth;
    }

    [Fact]
    public void Resolve_WhenConnectedWithPending_ShowsPendingPushNotSynced()
    {
        var display = ConnectionDisplayResolver.Resolve(
            CreateVerifiedAuth(),
            SyncStatus.Connected,
            hasConflicts: false,
            pendingChangeCount: 3);

        Assert.Equal(ConnectionDisplayState.PendingPush, display.State);
        Assert.Equal("有未推送 (3)", display.ShortLabel);
        Assert.Contains("3", display.StatusBarText);
        Assert.Contains("尚未推送", display.DetailText);
        Assert.DoesNotContain("已同步", display.ShortLabel);
        Assert.Equal(ThemeBrushKeys.Accent, display.BrushKey);
    }

    [Fact]
    public void Resolve_WhenConnectedWithoutPending_ShowsSynced()
    {
        var display = ConnectionDisplayResolver.Resolve(
            CreateVerifiedAuth(),
            SyncStatus.Connected,
            hasConflicts: false,
            pendingChangeCount: 0);

        Assert.Equal(ConnectionDisplayState.Synced, display.State);
        Assert.Equal("已同步", display.ShortLabel);
        Assert.Contains("无待推送", display.DetailText);
        Assert.Equal(ThemeBrushKeys.Success, display.BrushKey);
    }

    [Fact]
    public void Resolve_WhenPushingWithPending_StillShowsSyncing()
    {
        var display = ConnectionDisplayResolver.Resolve(
            CreateVerifiedAuth(),
            SyncStatus.Pushing,
            hasConflicts: false,
            pendingChangeCount: 2);

        Assert.Equal(ConnectionDisplayState.Syncing, display.State);
        Assert.Equal("同步中", display.ShortLabel);
    }

    [Fact]
    public void Resolve_ConflictOverridesPending()
    {
        var display = ConnectionDisplayResolver.Resolve(
            CreateVerifiedAuth(),
            SyncStatus.Connected,
            hasConflicts: true,
            conflictCount: 1,
            pendingChangeCount: 5);

        Assert.Equal(ConnectionDisplayState.Conflict, display.State);
        Assert.True(display.OpensConflicts);
    }
}
