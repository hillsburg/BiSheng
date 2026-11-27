using System.Windows.Controls;
using BiSheng.Latte.Services;

namespace BiSheng.Latte.Tests.Services;

/// <summary>验证下载完成后的线程切换和安装顺序，不启动真实更新进程。</summary>
public sealed class AppUpdateServiceTests
{
    /// <summary>下载结束后从后台调度到 UI，并等待全部准备工作才启动安装。</summary>
    [WpfFact]
    public async Task CompleteUpdate_WaitsForUiPreparationBeforeInstalling()
    {
        var status = new TextBlock();
        var dispatcher = status.Dispatcher;
        var download = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishPreparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int installCount = 0;

        var update = AppUpdateService.CompleteUpdateAsync(
            download.Task,
            async () =>
            {
                bool continuedInBackground = !dispatcher.CheckAccess();
                await dispatcher.InvokeAsync(() => status.Text = "即将重启以完成更新…").Task.ConfigureAwait(false);
                preparationStarted.SetResult(continuedInBackground);
                await finishPreparation.Task.ConfigureAwait(false);
            },
            () => Interlocked.Increment(ref installCount));

        Assert.False(preparationStarted.Task.IsCompleted);
        download.SetResult();
        try
        {
            Assert.True(await preparationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("即将重启以完成更新…", status.Text);
            Assert.Equal(0, Volatile.Read(ref installCount));
        }
        finally
        {
            finishPreparation.SetResult();
        }

        await update.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, installCount);
    }

    /// <summary>准备失败时保留错误给上层展示，并阻止安装进程启动。</summary>
    [Fact]
    public async Task CompleteUpdate_WhenPreparationFails_DoesNotInstall()
    {
        var failure = new InvalidOperationException("准备更新失败");
        bool installed = false;

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AppUpdateService.CompleteUpdateAsync(
                Task.CompletedTask,
                () => Task.FromException(failure),
                () => installed = true));

        Assert.Same(failure, observed);
        Assert.False(installed);
    }
}
