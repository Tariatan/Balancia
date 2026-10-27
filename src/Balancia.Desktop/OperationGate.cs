namespace Balancia.Desktop;

/// <summary>
/// The rules that keep window operations from overlapping: one runs at a time, a call that arrives while another runs is dropped,
/// and refreshes requested meanwhile collapse into one that runs when the operation ends. It holds no controls.
/// </summary>
internal sealed class OperationGate
{
    private bool refreshRequested;

    public bool IsBusy { get; private set; }

    /// <summary>Starts an operation; false when another one is running, in which case the call is dropped.</summary>
    public bool TryBegin()
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        return true;
    }

    /// <summary>Ends the running operation; true when a refresh was requested while it ran, which the caller should now run once.</summary>
    public bool End()
    {
        IsBusy = false;
        var pending = refreshRequested;
        refreshRequested = false;
        return pending;
    }

    /// <summary>A request to refresh: true when nothing is running and the caller should refresh now; false when it was kept for <see cref="End"/>.</summary>
    public bool RequestRefresh()
    {
        if (IsBusy)
        {
            refreshRequested = true;
            return false;
        }

        return true;
    }
}
