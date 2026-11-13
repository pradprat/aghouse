using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;

namespace AgHouse.App.Workspaces;

/// <summary>
/// The pull request for a branch, from the GitHub CLI (`gh pr view`). Shown in the sidebar as
/// "#123 open". Silent when gh is missing, signed out, or the branch has no PR: the row just shows
/// nothing. Results are cached per folder and branch for a few minutes.
/// </summary>
internal static class PullRequests
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    private static readonly Dictionary<string, (DateTime At, string? Text)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static bool _ghMissing;

    /// <summary>"#123 open", "#123 draft", "#123 merged", "#123 closed", or null.</summary>
    public static async Task<string?> LookupAsync(string? cwd, string? branch)
    {
        if (_ghMissing || string.IsNullOrEmpty(cwd) || string.IsNullOrEmpty(branch) || !Directory.Exists(cwd)) return null;
        var key = cwd + "|" + branch;
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < CacheFor) return hit.Text;
        }

        var text = await Task.Run(() => Query(cwd)).ConfigureAwait(false);
        lock (Cache) Cache[key] = (DateTime.UtcNow, text);
        return text;
    }

    private static string? Query(string cwd)
    {
        try
        {
            var psi = new ProcessStartInfo("gh", "pr view --json number,state,isDraft")
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.Environment["GH_PROMPT_DISABLED"] = "1";
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync(); // drain, or a chatty gh could block
            if (!p.WaitForExit(8000))
            {
                try { p.Kill(true); } catch { }
                return null;
            }
            if (p.ExitCode != 0) return null; // no PR for this branch, not a GitHub repo, or signed out

            var json = JsonNode.Parse(stdout.Result);
            var number = json?["number"]?.GetValue<int>();
            if (number is null) return null;
            var state = json?["state"]?.GetValue<string>()?.ToLowerInvariant();
            if (json?["isDraft"]?.GetValue<bool>() == true && state == "open") state = "draft";
            return $"#{number} {state}";
        }
        catch (Win32Exception)
        {
            _ghMissing = true; // gh isn't installed: stop asking
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException or IOException)
        {
            return null;
        }
    }
}
