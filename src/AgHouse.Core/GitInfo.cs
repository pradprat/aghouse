namespace AgHouse.Core;

public static class GitInfo
{
    /// <summary>Branch name (or short SHA when detached) for the repo containing <paramref name="dir"/>.</summary>
    public static string? TryGetBranch(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try
        {
            var current = new DirectoryInfo(dir);
            if (!current.Exists) return null;

            for (var d = current; d is not null; d = d.Parent)
            {
                var gitPath = Path.Combine(d.FullName, ".git");
                string? gitDir = null;

                if (Directory.Exists(gitPath)) gitDir = gitPath;
                else if (File.Exists(gitPath))
                {
                    // Worktree / submodule: ".git" is a file "gitdir: <path>"
                    var line = File.ReadLines(gitPath).FirstOrDefault()?.Trim();
                    if (line is not null && line.StartsWith("gitdir:", StringComparison.Ordinal))
                    {
                        var p = line["gitdir:".Length..].Trim();
                        gitDir = Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(d.FullName, p));
                    }
                }

                if (gitDir is null) continue;

                var headFile = Path.Combine(gitDir, "HEAD");
                if (!File.Exists(headFile)) return null;
                var head = File.ReadAllText(headFile).Trim();
                const string prefix = "ref: refs/heads/";
                if (head.StartsWith(prefix, StringComparison.Ordinal)) return head[prefix.Length..];
                if (head.StartsWith("ref: ", StringComparison.Ordinal)) return head["ref: ".Length..];
                return head.Length >= 7 ? head[..7] : head;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }
}
