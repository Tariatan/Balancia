using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class OperationRunnerTests
{
    private sealed class FakeHost : IOperationHost
    {
        public List<string> Events { get; } = [];
        public Queue<Action> Posted { get; } = new();
        public Func<bool>? BusyProbe { get; set; }
        public Exception? FailedWith { get; private set; }
        public Window? FailedOwner { get; private set; }

        public void WorkStarted() => Events.Add("started");

        public void WorkSucceeded() => Events.Add("succeeded");

        public Task WorkFailedAsync(Exception exception, Window? errorOwner)
        {
            FailedWith = exception;
            FailedOwner = errorOwner;
            Events.Add("failed");
            return Task.CompletedTask;
        }

        public void WorkFinished() => Events.Add(BusyProbe is null ? "finished" : $"finished(busy={BusyProbe()})");

        public void Post(Action action)
        {
            Events.Add("posted");
            Posted.Enqueue(action);
        }

        public void RunPosted()
        {
            while (Posted.TryDequeue(out var action))
            {
                action();
            }
        }
    }

    private static (OperationRunner Runner, FakeHost Host, Func<int> Refreshes) Create()
    {
        var host = new FakeHost();
        var refreshes = 0;
        var runner = new OperationRunner(host, () =>
        {
            refreshes++;
            return Task.CompletedTask;
        });
        return (runner, host, () => refreshes);
    }

    [Fact]
    public async Task RunAsync_Success_ShowsTheWorkRunsTheActionAndConfirmsIt()
    {
        // Arrange
        var (runner, host, _) = Create();
        var ran = 0;

        // Act
        await runner.RunAsync(() =>
        {
            ran++;
            return Task.CompletedTask;
        });

        // Assert
        Assert.Equal(1, ran);
        Assert.Equal(["started", "succeeded", "finished"], host.Events);
        Assert.False(runner.IsBusy);
    }

    [Fact]
    public async Task RunAsync_WithoutDisablingControls_RunsSilently()
    {
        // Arrange
        var (runner, host, _) = Create();
        var ran = 0;

        // Act
        await runner.RunAsync(() =>
        {
            ran++;
            return Task.CompletedTask;
        }, disableControls: false);

        // Assert
        Assert.Equal(1, ran);
        Assert.Empty(host.Events);
    }

    [Fact]
    public async Task RunAsync_WhileTheActionRuns_IsBusy()
    {
        // Arrange
        var (runner, _, _) = Create();
        var release = new TaskCompletionSource();
        bool? busyDuring = null;

        // Act
        var running = runner.RunAsync(async () =>
        {
            busyDuring = runner.IsBusy;
            await release.Task;
        });
        var busyWhileWaiting = runner.IsBusy;
        release.SetResult();
        await running;

        // Assert
        Assert.True(busyDuring);
        Assert.True(busyWhileWaiting);
        Assert.False(runner.IsBusy);
    }

    [Fact]
    public async Task RunAsync_WhileAnotherOperationRuns_DropsTheCallWithoutTouchingTheHost()
    {
        // Arrange
        var (runner, host, _) = Create();
        var release = new TaskCompletionSource();
        var first = runner.RunAsync(() => release.Task);
        host.Events.Clear();
        var secondRan = false;

        // Act
        await runner.RunAsync(() =>
        {
            secondRan = true;
            return Task.CompletedTask;
        });
        release.SetResult();
        await first;

        // Assert
        Assert.False(secondRan);
        Assert.Equal(["succeeded", "finished"], host.Events);
    }

    [Fact]
    public async Task RunAsync_ActionFails_ReportsItAndDoesNotThrow()
    {
        // Arrange
        var (runner, host, _) = Create();
        var failure = new InvalidOperationException("boom");

        // Act
        await runner.RunAsync(() => throw failure);

        // Assert
        Assert.Same(failure, host.FailedWith);
        Assert.Equal(["started", "failed", "finished"], host.Events);
        Assert.False(runner.IsBusy);
    }

    [AvaloniaFact]
    public async Task RunAsync_ActionFailsOverADialog_PassesThatDialogAsTheOwner()
    {
        // Arrange
        var (runner, host, _) = Create();
        var dialog = new Window();

        // Act
        await runner.RunAsync(() => throw new IOException("disk"), errorOwner: dialog);

        // Assert
        Assert.Same(dialog, host.FailedOwner);
    }

    [Fact]
    public async Task RunAsync_ActionFailsWithoutDisablingControls_StillReportsIt()
    {
        // Arrange
        var (runner, host, _) = Create();

        // Act
        await runner.RunAsync(() => throw new InvalidOperationException("boom"), disableControls: false);

        // Assert
        Assert.Equal(["failed"], host.Events);
    }

    [Fact]
    public async Task RunAsync_TheOperationIsNoLongerBusyWhenTheControlsComeBack()
    {
        // Arrange
        var (runner, host, _) = Create();
        host.BusyProbe = () => runner.IsBusy;

        // Act
        await runner.RunAsync(() => Task.CompletedTask);

        // Assert
        Assert.Contains("finished(busy=False)", host.Events);
    }

    [Fact]
    public async Task RunAsync_NoRefreshRequested_PostsNothing()
    {
        // Arrange
        var (runner, host, _) = Create();

        // Act
        await runner.RunAsync(() => Task.CompletedTask);

        // Assert
        Assert.DoesNotContain("posted", host.Events);
    }

    [Fact]
    public async Task RequestRefreshAsync_WhenIdle_RefreshesNowAndSilently()
    {
        // Arrange
        var (runner, host, refreshes) = Create();

        // Act
        await runner.RequestRefreshAsync();

        // Assert
        Assert.Equal(1, refreshes());
        Assert.Empty(host.Events);
        Assert.False(runner.IsBusy);
    }

    [Fact]
    public async Task RequestRefreshAsync_WhileBusy_WaitsAndThenRefreshesOnceHoweverOftenItWasRequested()
    {
        // Arrange
        var (runner, host, refreshes) = Create();
        var release = new TaskCompletionSource();
        var running = runner.RunAsync(() => release.Task);

        // Act
        await runner.RequestRefreshAsync();
        await runner.RequestRefreshAsync();
        await runner.RequestRefreshAsync();
        var beforeEnd = refreshes();
        release.SetResult();
        await running;
        var posted = host.Events.Count(e => e == "posted");
        host.RunPosted();

        // Assert
        Assert.Equal(0, beforeEnd);
        Assert.Equal(1, posted);
        Assert.Equal(1, refreshes());
    }

    [Fact]
    public async Task RequestRefreshAsync_WhileAnOperationFails_StillRefreshesAfterwards()
    {
        // Arrange
        var (runner, host, refreshes) = Create();
        var release = new TaskCompletionSource();
        var running = runner.RunAsync(async () =>
        {
            await release.Task;
            throw new InvalidOperationException("boom");
        });
        await runner.RequestRefreshAsync();

        // Act
        release.SetResult();
        await running;
        host.RunPosted();

        // Assert
        Assert.NotNull(host.FailedWith);
        Assert.Equal(1, refreshes());
    }

    [Fact]
    public async Task RequestRefreshAsync_WhileARefreshRuns_RunsAnotherAfterwards()
    {
        // Arrange
        var host = new FakeHost();
        var release = new TaskCompletionSource();
        var refreshes = 0;
        OperationRunner? runner = null;
        runner = new OperationRunner(host, async () =>
        {
            refreshes++;
            if (refreshes == 1)
            {
                await runner!.RequestRefreshAsync();
                await release.Task;
            }
        });

        // Act
        var first = runner.RequestRefreshAsync();
        release.SetResult();
        await first;
        host.RunPosted();

        // Assert
        Assert.Equal(2, refreshes);
        Assert.False(runner.IsBusy);
    }
}
