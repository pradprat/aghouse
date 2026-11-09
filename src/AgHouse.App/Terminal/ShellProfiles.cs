using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using AgHouse.App.Persistence;

namespace AgHouse.App.Terminal;

public sealed record ShellProfile(string Id, string Name, string CommandLine, IReadOnlyDictionary<string, string?> Env)
{
    public override string ToString() => Name;
}

/// <summary>Shells found on this machine. WSL distros are appended asynchronously.</summary>
public sealed class ShellProfiles
{
    private readonly AppSettings _settings;

    public ObservableCollection<ShellProfile> All { get; } = [];

    public ShellProfiles(AppSettings settings)
    {
        _settings = settings;
        var integration = Path.Combine(AppContext.BaseDirectory, "ShellIntegration", "aghouse.ps1");
        var psArgs = $"-NoLogo -NoExit -Command \". '{integration.Replace("'", "''")}'\"";
        var none = new Dictionary<string, string?>();

        var pwsh = FindOnPath("pwsh.exe") ?? Existing(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"));
        if (pwsh is not null) All.Add(new ShellProfile("pwsh", "PowerShell", $"\"{pwsh}\" {psArgs}", none));

        var winPs = Existing(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"));
        if (winPs is not null) All.Add(new ShellProfile("powershell", "Windows PowerShell", $"\"{winPs}\" {psArgs}", none));

        var cmd = Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
        // $e]9;9;<cwd>$e\ reports the working directory (ConEmu/Windows Terminal convention).
        All.Add(new ShellProfile("cmd", "Command Prompt", $"\"{cmd}\"",
            new Dictionary<string, string?> { ["PROMPT"] = "$e]9;9;$P$e\\$P$G" }));

        var gitBash = Existing(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"));
        if (gitBash is not null)
        {
            All.Add(new ShellProfile("gitbash", "Git Bash", $"\"{gitBash}\" --login -i",
                new Dictionary<string, string?> { ["PROMPT_COMMAND"] = "printf '\\033]9;9;%s\\007' \"$(pwd -W)\"" }));
        }
    }

    public ShellProfile Default =>
        All.FirstOrDefault(p => p.Id == _settings.DefaultProfile) ?? All[0];

    public ShellProfile Resolve(string? id) =>
        (id is null ? null : All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)
                                                    || string.Equals(p.Name, id, StringComparison.OrdinalIgnoreCase)))
        ?? Default;

    /// <summary>Adds installed WSL distros. Runs wsl.exe off the UI thread.</summary>
    public async Task DetectWslAsync()
    {
        var wsl = Existing(Path.Combine(Environment.SystemDirectory, "wsl.exe"));
        if (wsl is null) return;

        var distros = await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo(wsl, "--list --quiet")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.Unicode, // wsl.exe writes UTF-16
                };
                using var p = Process.Start(psi);
                if (p is null) return [];
                var output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(3000) || p.ExitCode != 0) return [];
                return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(d => d.Trim('\0'))
                    .Where(d => d.Length > 0 && !d.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                return Array.Empty<string>();
            }
        });

        foreach (var d in distros)
        {
            All.Add(new ShellProfile($"wsl-{d.ToLowerInvariant()}", d, $"\"{wsl}\" -d {d}", new Dictionary<string, string?>()));
        }
    }

    private static string? Existing(string path) => File.Exists(path) ? path : null;

    private static string? FindOnPath(string exe)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim().Trim('"'), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }
}
