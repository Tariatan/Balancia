using Avalonia.Controls;
using Serilog;

namespace Balancia.Desktop;

/// <summary>What the window does around an operation: show that work started, finished or failed, and run something later on the UI thread.</summary>
internal interface IOperationHost
{
    /// <summary>Disables the controls and shows that work is in progress.</summary>
    void WorkStarted();

    /// <summary>Called after an operation succeeded: confirms it, unless the operation already reported its own outcome.</summary>
    void WorkSucceeded();

    /// <summary>Shows why an operation failed, over <paramref name="errorOwner"/> when given. The operation counts as running until this returns.</summary>
    Task WorkFailedAsync(Exception exception, Window? errorOwner);

    /// <summary>Enables the controls again.</summary>
    void WorkFinished();

    /// <summary>Runs <paramref name="action"/> later on the UI thread.</summary>
    void Post(Action action);
}

/// <summary>
/// Runs the window's operations (loading, saving, importing, restoring, refreshing) one at a time. Around each it tells the host, logs a failure
/// instead of letting it escape, and afterwards runs the refresh that was requested while it ran. The window cannot be closed or driven by
/// shortcuts while <see cref="IsBusy"/>.
/// </summary>
internal sealed class OperationRunner(IOperationHost host, Func<Task> refreshOverview)
{
    private readonly OperationGate gate = new();

    public bool IsBusy => gate.IsBusy;

    /// <summary>
    /// Runs <paramref name="action"/> unless another operation is running, in which case the call is dropped. With <paramref name="disableControls"/> the host
    /// shows the work and confirms it afterwards; without it the operation runs silently, as an in-place refresh does.
    /// </summary>
    public async Task RunAsync(Func<Task> action, bool disableControls = true, Window? errorOwner = null, string? operationName = null)
    {
        if (!gate.TryBegin())
        {
            return;
        }

        if (disableControls)
        {
            host.WorkStarted();
        }

        try
        {
            await action();
            if (disableControls)
            {
                host.WorkSucceeded();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Desktop operation failed, Operation: '{Operation}'", operationName ?? action.Method.Name);
            await host.WorkFailedAsync(ex, errorOwner);
        }
        finally
        {
            var refreshPending = gate.End();
            if (disableControls)
            {
                host.WorkFinished();
            }

            if (refreshPending)
            {
                host.Post(() => _ = RequestRefreshAsync());
            }
        }
    }

    /// <summary>Refreshes the overview in place now, or once the running operation ends however many requests arrive meanwhile.</summary>
    public async Task RequestRefreshAsync()
    {
        if (!gate.RequestRefresh())
        {
            return;
        }

        await RunAsync(refreshOverview, disableControls: false);
    }
}
