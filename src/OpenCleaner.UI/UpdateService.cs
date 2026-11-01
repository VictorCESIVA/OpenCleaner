using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace OpenCleaner.UI;

public static class UpdateService
{
    private const string RepoUrl = "https://github.com/VictorCESIVA/OpenCleaner";
    private const long MaxLogBytes = 256 * 1024;
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);
    private static readonly object Gate = new();
    private static readonly object LogGate = new();
    private static readonly SemaphoreSlim CheckGate = new(1, 1);
    private static readonly UpdateStateMachine Machine = new(DefaultVersion());
    private static UpdateManager? _manager;
    private static UpdateInfo? _pendingInfo;
    private static CancellationTokenSource? _downloadCancellation;
    private static int _downloadGeneration;

    public static event Action<UpdateSnapshot>? StateChanged;

    internal static bool IsShuttingDown { get; private set; }

    public static UpdateSnapshot State
    {
        get { lock (Gate) return Machine.Snapshot; }
    }

    public static string? CurrentVersion => State.CurrentVersion;

    public static bool IsInstalled
    {
        get
        {
            try { return GetManager().IsInstalled; }
            catch (Exception ex)
            {
                Log("installed-check-failed " + Describe(ex));
                return false;
            }
        }
    }

    public static async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            UpdateManager manager = GetManager();
            if (!manager.IsInstalled)
            {
                Log("startup-portable-skip");
                return;
            }
            if (TryRestorePending(manager)) return;

            await RunCheckAsync(manager).ConfigureAwait(false);
            if (State.State == UpdateState.Available && App.Preferences.BackgroundUpdateDownload)
                _ = DownloadInBackgroundAsync();
        }
        catch (Exception ex)
        {
            Log("startup-check-failed " + Describe(ex));
        }
    }

    public static async Task<UpdateSnapshot> CheckNowAsync()
    {
        UpdateManager manager;
        try
        {
            manager = GetManager();
        }
        catch (Exception ex)
        {
            TryTransition(machine => machine.TryBeginCheck(), "manual-check-start");
            Fail(UpdateErrorCode.Unknown, ex);
            return State;
        }

        bool installed;
        try
        {
            installed = manager.IsInstalled;
        }
        catch (Exception ex)
        {
            TryTransition(machine => machine.TryBeginCheck(), "manual-check-start");
            Fail(MapError(ex), ex);
            return State;
        }
        if (!installed)
        {
            TryTransition(machine => machine.TryMarkNotInstalled(), "manual-not-installed");
            return State;
        }
        if (TryRestorePending(manager)) return State;

        await RunCheckAsync(manager).ConfigureAwait(false);
        return State;
    }

    public static async Task<UpdateSnapshot> DownloadAsync(CancellationToken cancellationToken = default)
    {
        UpdateInfo? info;
        CancellationTokenSource cancellation;
        UpdateSnapshot snapshot;
        int generation;
        lock (Gate)
        {
            info = _pendingInfo;
            if (info == null || !Machine.TryBeginDownload()) return Machine.Snapshot;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _downloadCancellation = cancellation;
            generation = ++_downloadGeneration;
            snapshot = Machine.Snapshot;
        }
        Raise(snapshot);
        Log($"download-start target={snapshot.TargetVersion ?? "?"}");

        using var timeout = new CancellationTokenSource(DownloadTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellation.Token, timeout.Token);
        try
        {
            await GetManager().DownloadUpdatesAsync(info, percent => ReportProgress(percent, generation),
                    linked.Token)
                .ConfigureAwait(false);

            lock (Gate)
            {
                if (generation != _downloadGeneration) return Machine.Snapshot;
                if (Machine.Snapshot.State != UpdateState.Downloading) return Machine.Snapshot;
                Machine.TryCompleteDownload();
                snapshot = Machine.Snapshot;
            }
            Raise(snapshot);
            Log($"download-complete target={snapshot.TargetVersion ?? "?"}");
        }
        catch (OperationCanceledException ex)
        {
            bool timedOut;
            bool current;
            lock (Gate)
            {
                current = generation == _downloadGeneration;
                timedOut = timeout.IsCancellationRequested
                           && Machine.Snapshot.State == UpdateState.Downloading;
            }
            if (!current)
            {
                Log("download-cancel-stale");
            }
            else if (timedOut)
            {
                Fail(UpdateErrorCode.Timeout, ex);
                Log("download-timeout");
            }
            else
            {
                TryTransition(machine => machine.TryCancelDownload(keepAvailable: true),
                    "download-cancelled");
            }
        }
        catch (Exception ex)
        {
            bool current;
            lock (Gate) current = generation == _downloadGeneration;
            if (current) Fail(MapError(ex), ex);
            else Log("download-failure-stale " + Describe(ex));
        }
        finally
        {
            lock (Gate)
            {
                if (ReferenceEquals(_downloadCancellation, cancellation))
                    _downloadCancellation = null;
            }
            cancellation.Dispose();
        }

        return State;
    }

    public static void CancelDownload()
    {
        CancellationTokenSource? cancellation;
        UpdateSnapshot snapshot;
        lock (Gate)
        {
            if (!Machine.TryCancelDownload(keepAvailable: true)) return;
            cancellation = _downloadCancellation;
            snapshot = Machine.Snapshot;
        }
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        Raise(snapshot);
        Log("download-cancel-requested");
    }

    public static bool ApplyAndRestart()
    {
        if (UiOperation.IsBusy)
        {
            Log("apply-blocked-busy");
            return false;
        }

        UpdateManager manager;
        UpdateSnapshot snapshot;
        VelopackAsset? asset;
        try
        {
            manager = GetManager();
        }
        catch (Exception ex)
        {
            Fail(UpdateErrorCode.Unknown, ex);
            return false;
        }

        lock (Gate) asset = _pendingInfo?.TargetFullRelease;
        if (asset == null)
        {
            try { asset = manager.UpdatePendingRestart; }
            catch (Exception ex)
            {
                Fail(UpdateErrorCode.Unknown, ex);
                return false;
            }
        }

        lock (Gate)
        {
            if (asset == null || !Machine.TryBeginApply(explicitUserAction: true))
                return false;
            snapshot = Machine.Snapshot;
        }
        Raise(snapshot);

        try
        {
            manager.WaitExitThenApplyUpdates(asset, silent: true, restart: true);
            Log($"apply-started target={snapshot.TargetVersion ?? asset.Version?.ToString() ?? "?"}");
            IsShuttingDown = true;
            System.Windows.Application.Current?.Shutdown();
            return true;
        }
        catch (Exception ex)
        {
            Fail(MapError(ex), ex);
            return false;
        }
    }

    public static void NotifyAppClosing()
    {
        UpdateSnapshot snapshot = State;
        if (snapshot.State == UpdateState.Downloading)
            Log($"app-closing-download-abandoned background={App.Preferences.BackgroundUpdateDownload}");
        else if (snapshot.State is UpdateState.Ready or UpdateState.Applying)
            Log($"app-closing-state={snapshot.State}");
    }

    private static async Task DownloadInBackgroundAsync()
    {
        try { await DownloadAsync().ConfigureAwait(false); }
        catch (Exception ex) { Log("background-download-failed " + Describe(ex)); }
    }

    private static async Task RunCheckAsync(UpdateManager manager)
    {
        if (!await CheckGate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (!TryTransition(machine => machine.TryBeginCheck(), "check-start")) return;
            lock (Gate) _pendingInfo = null;

            UpdateInfo? info;
            try
            {
                info = await CheckWithTimeoutAsync(manager).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Fail(MapError(ex), ex);
                return;
            }

            UpdateSnapshot snapshot;
            if (info?.TargetFullRelease is not { } asset)
            {
                lock (Gate)
                {
                    _pendingInfo = null;
                    Machine.TryCompleteCheckWithoutUpdate();
                    snapshot = Machine.Snapshot;
                }
                Raise(snapshot);
                Log("check-up-to-date");
                return;
            }

            string target = asset.Version?.ToString() ?? "";
            lock (Gate)
            {
                _pendingInfo = info;
                Machine.TryCompleteCheck(target, asset.NotesMarkdown);
                snapshot = Machine.Snapshot;
            }
            Raise(snapshot);
            Log($"check-available target={target}");
        }
        finally
        {
            CheckGate.Release();
        }
    }

    private static async Task<UpdateInfo?> CheckWithTimeoutAsync(UpdateManager manager)
    {
        Task<UpdateInfo?> check = manager.CheckForUpdatesAsync();
        Task finished = await Task.WhenAny(check, Task.Delay(CheckTimeout)).ConfigureAwait(false);
        if (!ReferenceEquals(finished, check))
        {
            _ = check.ContinueWith(static task => _ = task.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
            throw new TimeoutException();
        }
        return await check.ConfigureAwait(false);
    }

    private static void ReportProgress(int percent, int generation)
    {
        UpdateSnapshot snapshot;
        lock (Gate)
        {
            if (generation != _downloadGeneration) return;
            if (!Machine.TryReportDownloadProgress(percent)) return;
            snapshot = Machine.Snapshot;
        }
        Raise(snapshot);
    }

    private static bool TryRestorePending(UpdateManager manager)
    {
        VelopackAsset? pending;
        try { pending = manager.UpdatePendingRestart; }
        catch (Exception ex)
        {
            Log("pending-restart-check-failed " + Describe(ex));
            return false;
        }
        if (pending == null) return false;

        UpdateSnapshot snapshot;
        lock (Gate)
        {
            if (!Machine.TryRestorePending(pending.Version?.ToString() ?? "",
                    pending.NotesMarkdown)) return false;
            snapshot = Machine.Snapshot;
        }
        Raise(snapshot);
        Log($"pending-restart target={snapshot.TargetVersion ?? "?"}");
        return true;
    }

    private static UpdateManager GetManager()
    {
        lock (Gate)
        {
            if (_manager != null) return _manager;
            var manager = new UpdateManager(new GithubSource(RepoUrl, null!, false));
            Machine.SetCurrentVersion(manager.CurrentVersion?.ToString() ?? DefaultVersion());
            _manager = manager;
            return manager;
        }
    }

    private static string? DefaultVersion() =>
        typeof(UpdateService).Assembly.GetName().Version?.ToString(3);

    private static bool TryTransition(Func<UpdateStateMachine, bool> transition, string reason)
    {
        UpdateSnapshot snapshot;
        lock (Gate)
        {
            if (!transition(Machine)) return false;
            snapshot = Machine.Snapshot;
        }
        Raise(snapshot);
        Log($"state={snapshot.State} reason={reason}");
        return true;
    }

    private static void Fail(UpdateErrorCode code, Exception? exception)
    {
        TryTransition(machine => machine.TryFail(code), "failure");
        Log($"error code={code} {Describe(exception)}");
    }

    private static void Raise(UpdateSnapshot snapshot)
    {
        try { StateChanged?.Invoke(snapshot); }
        catch (Exception ex) { Log("state-handler-failed " + Describe(ex)); }
    }

    private static UpdateErrorCode MapError(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            switch (current)
            {
                case NotInstalledException: return UpdateErrorCode.NotInstalled;
                case ChecksumFailedException: return UpdateErrorCode.Checksum;
                case AcquireLockFailedException: return UpdateErrorCode.Lock;
                case TimeoutException: return UpdateErrorCode.Timeout;
                case UnauthorizedAccessException: return UpdateErrorCode.DiskPermission;
                case HttpRequestException or WebException or SocketException:
                    return UpdateErrorCode.Network;
            }
        }

        string text = exception.ToString().ToLowerInvariant();
        if (text.Contains("checksum") || text.Contains("sha1") || text.Contains("sha256")
            || text.Contains("hash mismatch"))
            return UpdateErrorCode.Checksum;
        if (text.Contains("lock") || text.Contains("in use"))
            return UpdateErrorCode.Lock;
        if (text.Contains("permission") || text.Contains("access is denied")
            || text.Contains("access denied") || text.Contains("disk full")
            || text.Contains("no space") || text.Contains("not enough space"))
            return UpdateErrorCode.DiskPermission;
        if (text.Contains("timeout") || text.Contains("timed out"))
            return UpdateErrorCode.Timeout;
        if (text.Contains("network") || text.Contains("connect") || text.Contains("http")
            || text.Contains("socket") || text.Contains("ssl") || text.Contains("name resolution"))
            return UpdateErrorCode.Network;
        return UpdateErrorCode.Unknown;
    }

    private static string Describe(Exception? exception) =>
        exception == null
            ? ""
            : $"type={exception.GetType().Name} hresult=0x{exception.HResult:X8} message={Redact(exception.Message)}";

    private static string Redact(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "";
        string text = message;
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
            text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        text = text.Replace("\r", " ").Replace("\n", " ");
        return text.Length > 500 ? text[..500] + "…" : text;
    }

    private static void Log(string message)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenCleaner", "logs");
            lock (LogGate)
            {
                Directory.CreateDirectory(directory);
                string file = Path.Combine(directory, "update.log");
                var info = new FileInfo(file);
                if (info.Exists && info.Length > MaxLogBytes)
                    File.Move(file, file + ".1", overwrite: true);
                File.AppendAllText(file,
                    $"{DateTimeOffset.Now:O} {Redact(message)}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
