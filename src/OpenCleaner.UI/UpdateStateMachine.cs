namespace OpenCleaner.UI;

public enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Ready,
    Applying,
    Error,
    NotInstalled
}

public enum UpdateErrorCode
{
    None,
    NotInstalled,
    Network,
    Timeout,
    Checksum,
    Lock,
    DiskPermission,
    Unknown
}

public sealed record UpdateSnapshot(
    UpdateState State,
    string? CurrentVersion,
    string? TargetVersion,
    int Percent,
    string? Notes,
    UpdateErrorCode ErrorCode,
    bool CanCancel)
{
    public static UpdateSnapshot Initial(string? currentVersion = null) =>
        new(UpdateState.Idle, currentVersion, null, 0, null, UpdateErrorCode.None, false);
}

public sealed class UpdateStateMachine
{
    private UpdateSnapshot _snapshot;

    public UpdateStateMachine(string? currentVersion = null) =>
        _snapshot = UpdateSnapshot.Initial(currentVersion);

    public UpdateSnapshot Snapshot => _snapshot;

    public void SetCurrentVersion(string? version) =>
        _snapshot = _snapshot with { CurrentVersion = version };

    public bool TryBeginCheck()
    {
        if (_snapshot.State is not (UpdateState.Idle or UpdateState.UpToDate
            or UpdateState.Available or UpdateState.Error or UpdateState.NotInstalled))
            return false;
        _snapshot = _snapshot with
        {
            State = UpdateState.Checking,
            TargetVersion = null,
            Percent = 0,
            Notes = null,
            ErrorCode = UpdateErrorCode.None,
            CanCancel = false
        };
        return true;
    }

    public bool TryCompleteCheck(string targetVersion, string? notes)
    {
        if (_snapshot.State != UpdateState.Checking) return false;
        _snapshot = _snapshot with
        {
            State = UpdateState.Available,
            TargetVersion = targetVersion,
            Percent = 0,
            Notes = notes,
            ErrorCode = UpdateErrorCode.None,
            CanCancel = false
        };
        return true;
    }

    public bool TryCompleteCheckNoUpdate() => TryCompleteCheckWithoutUpdate();

    public bool TryCompleteCheckWithoutUpdate()
    {
        if (_snapshot.State != UpdateState.Checking) return false;
        _snapshot = _snapshot with
        {
            State = UpdateState.UpToDate,
            TargetVersion = null,
            Percent = 0,
            Notes = null,
            ErrorCode = UpdateErrorCode.None,
            CanCancel = false
        };
        return true;
    }

    public bool TryMarkNotInstalled()
    {
        if (_snapshot.State == UpdateState.Applying) return false;
        if (_snapshot.State == UpdateState.NotInstalled) return false;
        _snapshot = _snapshot with
        {
            State = UpdateState.NotInstalled,
            TargetVersion = null,
            Percent = 0,
            Notes = null,
            ErrorCode = UpdateErrorCode.NotInstalled,
            CanCancel = false
        };
        return true;
    }

    public bool TryBeginDownload()
    {
        if (_snapshot.State != UpdateState.Available) return false;
        _snapshot = _snapshot with
        {
            State = UpdateState.Downloading,
            Percent = 0,
            ErrorCode = UpdateErrorCode.None,
            CanCancel = true
        };
        return true;
    }

    public bool TryReportDownloadProgress(int percent)
    {
        if (_snapshot.State != UpdateState.Downloading) return false;
        int clamped = Math.Clamp(percent, 0, 100);
        if (clamped < _snapshot.Percent) return false;
        _snapshot = _snapshot with { Percent = clamped };
        return true;
    }

    public bool TryCompleteDownload()
    {
        if (_snapshot.State != UpdateState.Downloading) return false;
        _snapshot = _snapshot with
        {
            State = UpdateState.Ready,
            Percent = 100,
            ErrorCode = UpdateErrorCode.None,
            CanCancel = false
        };
        return true;
    }

    public bool TryCancelDownload() => TryCancelDownload(keepAvailable: true);

    public bool TryCancelDownload(bool keepAvailable)
    {
        if (_snapshot.State != UpdateState.Downloading) return false;
        bool keep = keepAvailable && !string.IsNullOrWhiteSpace(_snapshot.TargetVersion);
        _snapshot = _snapshot with
        {
            State = keep ? UpdateState.Available : UpdateState.Idle,
            TargetVersion = keep ? _snapshot.TargetVersion : null,
            Percent = 0,
            ErrorCode = UpdateErrorCode.None,
            CanCancel = false,
            Notes = keep ? _snapshot.Notes : null
        };
        return true;
    }

    public bool TryFail(UpdateErrorCode code)
    {
        if (code == UpdateErrorCode.NotInstalled && _snapshot.State != UpdateState.Applying)
            return TryMarkNotInstalled();
        if (_snapshot.State is not (UpdateState.Checking or UpdateState.Downloading or UpdateState.Applying))
            return false;
        _snapshot = _snapshot with
        {
            State = UpdateState.Error,
            ErrorCode = code == UpdateErrorCode.None ? UpdateErrorCode.Unknown : code,
            CanCancel = false
        };
        return true;
    }

    public bool TryBeginApply(bool explicitUserAction)
    {
        if (!explicitUserAction || _snapshot.State != UpdateState.Ready) return false;
        _snapshot = _snapshot with
        {
            State = UpdateState.Applying,
            CanCancel = false
        };
        return true;
    }

    public bool TryRestorePending(string targetVersion, string? notes)
    {
        if (_snapshot.State != UpdateState.Idle) return false;
        _snapshot = _snapshot with
        {
            State = UpdateState.Ready,
            TargetVersion = targetVersion,
            Percent = 100,
            Notes = notes,
            ErrorCode = UpdateErrorCode.None,
            CanCancel = false
        };
        return true;
    }

    public bool TryReset()
    {
        if (_snapshot.State == UpdateState.Applying) return false;
        _snapshot = UpdateSnapshot.Initial(_snapshot.CurrentVersion);
        return true;
    }
}
