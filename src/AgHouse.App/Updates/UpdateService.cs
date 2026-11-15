using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows.Threading;
using AgHouse.Core.Updates;

namespace AgHouse.App.Updates;

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Ready, Failed }

/// <summary>
/// Finds newer aghouse releases on GitHub and installs them. With <c>updates: auto</c> the installer
/// downloads in the background, is checked against the release's SHA256SUMS.txt, and runs when
/// aghouse quits, so nothing running in a pane is cut short. "Restart to update" does it now.
/// Only an installed aghouse updates itself; a portable or development build just says where the
/// new version is. All members are used on the UI thread.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const long MaxInstallerBytes = 500L * 1024 * 1024;

    private readonly Func<string> _mode;
    private readonly HttpClient _http;
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _work;
    private string? _installerPath;
    private string? _expectedHash;
    private bool _relaunch;
    private bool _installOnExit;

    public UpdateService(Func<string> mode)
    {
        _mode = mode;
        Current = ReleaseFeed.CurrentVersion(typeof(UpdateService).Assembly);
        _http = ReleaseFeed.CreateClient(Current);
        _timer = new DispatcherTimer { Interval = FirstCheck };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = Interval;
            if (Mode != "off") await CheckAsync(automatic: true);
        };
        IsInstalled = Directory.EnumerateFiles(AppContext.BaseDirectory, "unins*.exe").Any();
        CleanUpOldDownloads();
    }

    public Version Current { get; }

    /// <summary>aghouse was installed by its installer (an uninstaller sits next to it), so it can update itself.</summary>
    public bool IsInstalled { get; }

    public UpdateState State { get; private set; }

    /// <summary>The newest release once a check found one newer than this build.</summary>
    public ReleaseInfo? Latest { get; private set; }

    public DateTime? LastChecked { get; private set; }

    public int Progress { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Something above changed.</summary>
    public event Action? Changed;

    private string Mode => _mode() is "notify" or "off" ? _mode() : "auto";

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "aghouse", "updates");

    public void Start() => _timer.Start();

    /// <summary>The updates setting changed: an "off" that became on checks soon.</summary>
    public void SettingChanged()
    {
        if (Mode == "off" || State is UpdateState.Checking or UpdateState.Downloading) return;
        if (State == UpdateState.Available && Mode == "auto" && IsInstalled)
        {
            _ = DownloadAsync();
            return;
        }
        _timer.Stop();
        _timer.Interval = State == UpdateState.Idle ? FirstCheck : Interval;
        _timer.Start();
    }

    /// <summary>
    /// Asks GitHub for the newest release. In auto mode on an installed build, a newer one downloads
    /// straight away. <paramref name="automatic"/> checks keep quiet about network errors in the log only.
    /// </summary>
    public async Task CheckAsync(bool automatic = false)
    {
        if (State is UpdateState.Checking or UpdateState.Downloading) return;
        if (State == UpdateState.Ready) return; // already downloaded; nothing newer matters until it's installed
        Set(UpdateState.Checking);
        try
        {
            var latest = await ReleaseFeed.FetchLatestAsync(_http, CancellationToken.None);
            LastChecked = DateTime.Now;
            if (latest is null || latest.Version <= Current)
            {
                Latest = null;
                Set(UpdateState.UpToDate);
                return;
            }
            Latest = latest;
            Log.Write($"update: {latest.Version} is out (this is {Current})");
            Set(UpdateState.Available);
            if (Mode == "auto" && IsInstalled && latest.CanInstall) await DownloadAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Write($"update: check failed: {ex.Message}");
            Error = automatic ? null : "Couldn't reach GitHub. Check your connection and try again.";
            Set(automatic ? (Latest is null ? UpdateState.Idle : UpdateState.Available) : UpdateState.Failed);
        }
    }

    /// <summary>Downloads the installer for <see cref="Latest"/> and checks its SHA-256. True when it's ready to run.</summary>
    public async Task<bool> DownloadAsync()
    {
        if (Latest is not { CanInstall: true } release || !IsInstalled) return false;
        if (State == UpdateState.Ready) return true;
        if (State == UpdateState.Downloading) return false;
        _work?.Cancel();
        _work = new CancellationTokenSource();
        var ct = _work.Token;
        Progress = 0;
        Set(UpdateState.Downloading);
        var target = Path.Combine(Dir, release.SetupName!);
        var partial = target + ".partial";
        try
        {
            Directory.CreateDirectory(Dir);
            var sums = await _http.GetStringAsync(release.ChecksumsUrl, ct);
            var expected = ReleaseFeed.FindChecksum(sums, release.SetupName!)
                           ?? throw new InvalidDataException($"{ReleaseFeed.ChecksumsName} doesn't list {release.SetupName}.");

            // Downloaded earlier (an update that was never installed): use it if it's still the right file.
            if (File.Exists(target) && await HashAsync(target, ct) == expected)
            {
                return Finish(target, expected);
            }

            using (var response = await _http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? release.SetupSize;
                if (total > MaxInstallerBytes) throw new InvalidDataException("The installer is larger than expected.");
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    done += read;
                    if (done > MaxInstallerBytes) throw new InvalidDataException("The installer is larger than expected.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    var percent = total > 0 ? (int)(done * 100 / total) : 0;
                    if (percent != Progress) { Progress = percent; Changed?.Invoke(); }
                }
            }

            if (await HashAsync(partial, ct) != expected)
            {
                throw new InvalidDataException("The download doesn't match the release's checksum.");
            }
            File.Move(partial, target, overwrite: true);
            return Finish(target, expected);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
                                       or UnauthorizedAccessException or OperationCanceledException)
        {
            TryDelete(partial);
            Log.Write($"update: download failed: {ex.Message}");
            Error = ex is InvalidDataException ? ex.Message : "The download didn't finish. Try again later.";
            Set(UpdateState.Failed);
            return false;
        }

        bool Finish(string path, string hash)
        {
            _installerPath = path;
            _expectedHash = hash;
            Log.Write($"update: {release.Version} downloaded and verified ({path})");
            Set(UpdateState.Ready);
            return true;
        }
    }

    /// <summary>
    /// Run the installer once aghouse has quit. <paramref name="relaunch"/> starts the new version
    /// afterwards (Restart to update); without it, the update installs quietly after a normal quit.
    /// </summary>
    public void InstallOnExit(bool relaunch)
    {
        _installOnExit = true;
        _relaunch = relaunch;
    }

    /// <summary>The quit was called off (the user kept a running program): don't install after all.</summary>
    public void CancelInstallOnExit()
    {
        _installOnExit = false;
        _relaunch = false;
    }

    /// <summary>
    /// Called as aghouse exits. Starts the installer when one is ready and either the user asked for it
    /// or updates are automatic. Never during a Windows sign-out or shutdown.
    /// </summary>
    public void RunPendingInstall(bool sessionEnding)
    {
        if (sessionEnding || State != UpdateState.Ready || _installerPath is not { } path) return;
        if (!_installOnExit && Mode != "auto") return;
        try
        {
            // Hash once more: the file sat in a folder for a while.
            if (_expectedHash is null || HashAsync(path, CancellationToken.None).GetAwaiter().GetResult() != _expectedHash)
            {
                Log.Write("update: installer changed since it was verified; not running it");
                return;
            }
            var scope = IsUnder(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
                ? "/ALLUSERS"
                : "/CURRENTUSER";
            var args = $"/SP- /SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS {scope}" + (_relaunch ? " /relaunch=1" : "");
            Log.Write($"update: running {path} {args}");
            Process.Start(new ProcessStartInfo(path, args) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            // Win32Exception 1223: the user said no to the admin prompt for an all-users install.
            Log.Write($"update: couldn't start the installer: {ex.Message}");
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }

    /// <summary>Installers for this version or older are done with; half-finished downloads too.</summary>
    private void CleanUpOldDownloads()
    {
        try
        {
            if (!Directory.Exists(Dir)) return;
            foreach (var file in Directory.EnumerateFiles(Dir))
            {
                var name = Path.GetFileName(file);
                var version = name.StartsWith("aghouse-", StringComparison.Ordinal) && name.EndsWith("-setup-x64.exe", StringComparison.Ordinal)
                    ? ReleaseFeed.ParseVersion(name["aghouse-".Length..^"-setup-x64.exe".Length])
                    : null;
                if (version is null || version <= Current) TryDelete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"update: cleanup failed: {ex.Message}");
        }
    }

    private static bool IsUnder(string path, string folder) =>
        folder.Length > 0 && Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void Set(UpdateState state)
    {
        if (state != UpdateState.Failed) Error = null;
        State = state;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _timer.Stop();
        _work?.Cancel();
        _http.Dispose();
    }
}
