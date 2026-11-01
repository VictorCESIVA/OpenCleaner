using OpenCleaner.UI;
using Xunit;

namespace OpenCleaner.UI.Tests;

public sealed class UpdateStateMachineTests
{
    private static UpdateStateMachine Available(string notes = "notes")
    {
        var machine = new UpdateStateMachine("1.0.0");
        Assert.True(machine.TryBeginCheck());
        Assert.True(machine.TryCompleteCheck("2.0.0", notes));
        return machine;
    }

    [Fact]
    public void StartsIdleWithCurrentVersion()
    {
        var machine = new UpdateStateMachine("1.0.0");

        Assert.Equal(UpdateState.Idle, machine.Snapshot.State);
        Assert.Equal("1.0.0", machine.Snapshot.CurrentVersion);
        Assert.Equal(0, machine.Snapshot.Percent);
        Assert.False(machine.Snapshot.CanCancel);
    }

    [Fact]
    public void IdleToCheckingToUpToDate()
    {
        var machine = new UpdateStateMachine("1.0.0");

        Assert.True(machine.TryBeginCheck());
        Assert.Equal(UpdateState.Checking, machine.Snapshot.State);
        Assert.True(machine.TryCompleteCheckWithoutUpdate());
        Assert.Equal(UpdateState.UpToDate, machine.Snapshot.State);
        Assert.Null(machine.Snapshot.TargetVersion);
    }

    [Fact]
    public void UpdateBecomesAvailableWithTargetAndNotes()
    {
        UpdateStateMachine machine = Available("Fix **important**");

        Assert.Equal(UpdateState.Available, machine.Snapshot.State);
        Assert.Equal("2.0.0", machine.Snapshot.TargetVersion);
        Assert.Equal("Fix **important**", machine.Snapshot.Notes);
        Assert.False(machine.Snapshot.CanCancel);
    }

    [Fact]
    public void DownloadFlowReachesReady()
    {
        UpdateStateMachine machine = Available();

        Assert.True(machine.TryBeginDownload());
        Assert.Equal(UpdateState.Downloading, machine.Snapshot.State);
        Assert.True(machine.Snapshot.CanCancel);
        Assert.True(machine.TryReportDownloadProgress(42));
        Assert.Equal(42, machine.Snapshot.Percent);
        Assert.True(machine.TryCompleteDownload());
        Assert.Equal(UpdateState.Ready, machine.Snapshot.State);
        Assert.Equal(100, machine.Snapshot.Percent);
        Assert.False(machine.Snapshot.CanCancel);
    }

    [Fact]
    public void CancellingDownloadKeepsAvailableTarget()
    {
        UpdateStateMachine machine = Available();
        Assert.True(machine.TryBeginDownload());

        Assert.True(machine.TryCancelDownload(keepAvailable: true));

        Assert.Equal(UpdateState.Available, machine.Snapshot.State);
        Assert.Equal("2.0.0", machine.Snapshot.TargetVersion);
        Assert.Equal(0, machine.Snapshot.Percent);
        Assert.False(machine.Snapshot.CanCancel);
    }

    [Fact]
    public void CancellingWithoutKeepingAvailableReturnsIdle()
    {
        UpdateStateMachine machine = Available();
        Assert.True(machine.TryBeginDownload());

        Assert.True(machine.TryCancelDownload(keepAvailable: false));

        Assert.Equal(UpdateState.Idle, machine.Snapshot.State);
        Assert.Null(machine.Snapshot.TargetVersion);
    }

    [Fact]
    public void ReadyDoesNotApplyWithoutExplicitUserAction()
    {
        UpdateStateMachine machine = Available();
        machine.TryBeginDownload();
        machine.TryCompleteDownload();

        Assert.False(machine.TryBeginApply(explicitUserAction: false));
        Assert.Equal(UpdateState.Ready, machine.Snapshot.State);
        Assert.True(machine.TryBeginApply(explicitUserAction: true));
        Assert.Equal(UpdateState.Applying, machine.Snapshot.State);
    }

    [Fact]
    public void ApplyIsRejectedOutsideReady()
    {
        var machine = new UpdateStateMachine("1.0.0");

        Assert.False(machine.TryBeginApply(explicitUserAction: true));
        Assert.Equal(UpdateState.Idle, machine.Snapshot.State);
    }

    [Fact]
    public void ErrorCanRetry()
    {
        var machine = new UpdateStateMachine("1.0.0");
        machine.TryBeginCheck();

        Assert.True(machine.TryFail(UpdateErrorCode.Network));
        Assert.Equal(UpdateState.Error, machine.Snapshot.State);
        Assert.Equal(UpdateErrorCode.Network, machine.Snapshot.ErrorCode);
        Assert.True(machine.TryBeginCheck());
        Assert.Equal(UpdateState.Checking, machine.Snapshot.State);
        Assert.Equal(UpdateErrorCode.None, machine.Snapshot.ErrorCode);
    }

    [Fact]
    public void NotInstalledHasDedicatedState()
    {
        var machine = new UpdateStateMachine("1.0.0");

        Assert.True(machine.TryMarkNotInstalled());
        Assert.Equal(UpdateState.NotInstalled, machine.Snapshot.State);
        Assert.Equal(UpdateErrorCode.NotInstalled, machine.Snapshot.ErrorCode);
        Assert.False(machine.TryMarkNotInstalled());
    }

    [Fact]
    public void PendingRestartIsReadyWithoutGoingOnline()
    {
        var machine = new UpdateStateMachine("1.0.0");

        Assert.True(machine.TryRestorePending("2.0.0", "notes"));

        Assert.Equal(UpdateState.Ready, machine.Snapshot.State);
        Assert.Equal("2.0.0", machine.Snapshot.TargetVersion);
        Assert.Equal(100, machine.Snapshot.Percent);
        Assert.True(machine.TryBeginApply(explicitUserAction: true));
    }

    [Fact]
    public void StaleCheckResultIsIgnored()
    {
        var machine = new UpdateStateMachine("1.0.0");
        machine.TryBeginCheck();
        machine.TryFail(UpdateErrorCode.Timeout);

        Assert.False(machine.TryCompleteCheck("2.0.0", "late"));
        Assert.False(machine.TryCompleteCheckWithoutUpdate());
        Assert.Equal(UpdateState.Error, machine.Snapshot.State);
        Assert.Null(machine.Snapshot.TargetVersion);
    }

    [Fact]
    public void StaleProgressAndCompletionAreIgnored()
    {
        UpdateStateMachine machine = Available();
        machine.TryBeginDownload();
        machine.TryCancelDownload(keepAvailable: true);

        Assert.False(machine.TryReportDownloadProgress(80));
        Assert.False(machine.TryCompleteDownload());
        Assert.Equal(UpdateState.Available, machine.Snapshot.State);
        Assert.Equal(0, machine.Snapshot.Percent);
    }

    [Fact]
    public void ProgressIsClampedAndDoesNotGoBackwards()
    {
        UpdateStateMachine machine = Available();
        machine.TryBeginDownload();

        Assert.True(machine.TryReportDownloadProgress(150));
        Assert.Equal(100, machine.Snapshot.Percent);
        Assert.False(machine.TryReportDownloadProgress(90));
        Assert.Equal(100, machine.Snapshot.Percent);
        Assert.False(machine.TryReportDownloadProgress(-5));
    }

    [Fact]
    public void FailureCannotOverrideReadyOrIdle()
    {
        var idle = new UpdateStateMachine("1.0.0");
        Assert.False(idle.TryFail(UpdateErrorCode.Unknown));

        UpdateStateMachine ready = Available();
        ready.TryBeginDownload();
        ready.TryCompleteDownload();
        Assert.False(ready.TryFail(UpdateErrorCode.Network));
        Assert.Equal(UpdateState.Ready, ready.Snapshot.State);
    }

    [Fact]
    public void ResetPreservesCurrentVersion()
    {
        UpdateStateMachine machine = Available();
        machine.TryBeginDownload();

        Assert.True(machine.TryReset());
        Assert.Equal(UpdateState.Idle, machine.Snapshot.State);
        Assert.Equal("1.0.0", machine.Snapshot.CurrentVersion);
        Assert.Null(machine.Snapshot.TargetVersion);
    }
}
