using BiSheng.Latte.Services;
using BiSheng.Shared;

namespace BiSheng.Latte.Tests.Services;

public class ConflictDialogCopyTests
{
    [Theory]
    [InlineData(ChangeActions.Update, "更新")]
    [InlineData(ChangeActions.Delete, "删除")]
    [InlineData(ChangeActions.Create, "创建")]
    public void FormatAction_MapsKnownActions(string action, string expected)
    {
        Assert.Equal(expected, ConflictDialogCopy.FormatAction(action));
    }

    [Fact]
    public void KeepButtons_UseDeleteAwareCopy()
    {
        Assert.Equal("保留本机删除", ConflictDialogCopy.KeepLocalButton(ChangeActions.Delete));
        Assert.Equal("采用远端删除", ConflictDialogCopy.KeepRemoteButton(ChangeActions.Update, ChangeActions.Delete));
        Assert.Equal("恢复远端内容", ConflictDialogCopy.KeepRemoteButton(ChangeActions.Delete, ChangeActions.Update));
        Assert.Equal("保留远端版本", ConflictDialogCopy.KeepRemoteButton(ChangeActions.Update, ChangeActions.Update));
    }
}
