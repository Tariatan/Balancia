using Xunit;

namespace Balancia.Desktop.Tests;

public sealed class OperationGateTests
{
    [Fact]
    public void NewGate_IsIdle()
    {
        // Act
        var gate = new OperationGate();

        // Assert
        Assert.False(gate.IsBusy);
    }

    [Fact]
    public void TryBegin_WhenIdle_StartsAndMarksBusy()
    {
        // Arrange
        var gate = new OperationGate();

        // Act
        var started = gate.TryBegin();

        // Assert
        Assert.True(started);
        Assert.True(gate.IsBusy);
    }

    [Fact]
    public void TryBegin_WhileBusy_IsRefusedAndStaysBusy()
    {
        // Arrange
        var gate = new OperationGate();
        gate.TryBegin();

        // Act
        var started = gate.TryBegin();

        // Assert
        Assert.False(started);
        Assert.True(gate.IsBusy);
    }

    [Fact]
    public void End_ClearsBusyAndAllowsTheNextOperation()
    {
        // Arrange
        var gate = new OperationGate();
        gate.TryBegin();

        // Act
        var refreshPending = gate.End();

        // Assert
        Assert.False(refreshPending);
        Assert.False(gate.IsBusy);
        Assert.True(gate.TryBegin());
    }

    [Fact]
    public void RequestRefresh_WhenIdle_SaysToRefreshNowAndRecordsNothing()
    {
        // Arrange
        var gate = new OperationGate();

        // Act
        var refreshNow = gate.RequestRefresh();
        gate.TryBegin();

        // Assert
        Assert.True(refreshNow);
        Assert.False(gate.End());
    }

    [Fact]
    public void RequestRefresh_WhileBusy_IsKeptAndReportedOnceWhenTheOperationEnds()
    {
        // Arrange
        var gate = new OperationGate();
        gate.TryBegin();

        // Act
        var first = gate.RequestRefresh();
        var second = gate.RequestRefresh();
        var third = gate.RequestRefresh();
        var firstEnd = gate.End();
        gate.TryBegin();
        var secondEnd = gate.End();

        // Assert
        Assert.False(first);
        Assert.False(second);
        Assert.False(third);
        Assert.True(firstEnd);
        Assert.False(secondEnd);
    }

    [Fact]
    public void RequestRefresh_AfterTheOperationEnded_RunsNowAgain()
    {
        // Arrange
        var gate = new OperationGate();
        gate.TryBegin();
        gate.RequestRefresh();
        gate.End();

        // Act
        var refreshNow = gate.RequestRefresh();

        // Assert
        Assert.True(refreshNow);
    }
}
