namespace AgHouse.Core;

/// <summary>
/// The git repo a folder is in. <see cref="Root"/> is the working tree holding the folder (a worktree's
/// own folder); <see cref="CommonDir"/> is the repo's shared .git, the same for all of its worktrees;
/// <see cref="Name"/> is the main repo's folder name, so a worktree is named after the repo it belongs to.
/// </summary>
public sealed record RepoInfo(string Root, string CommonDir, string Name, string? Branch);

public static class GitInfo
{
    /// <summary>Branch name (or short SHA when detached) for the repo containing <paramref name="dir"/>.</summary>
    public static string? TryGetBranch(string? dir) => TryGetRepo(dir)?.Branch;

    /// <summary>The repo containing <paramref name="dir"/>, or null when it isn't in one (or can't be read).</summary>
    public static RepoInfo? TryGetRepo(string? dir)
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
                gitDir = Path.GetFullPath(gitDir);
                var common = CommonDir(gitDir);
                return new RepoInfo(d.FullName, common, RepoName(common, d.FullName), ReadBranch(gitDir));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>A worktree's git dir names the repo's shared one in "commondir"; a plain repo is its own.</summary>
    private static string CommonDir(string gitDir)
    {
        var file = Path.Combine(gitDir, "commondir");
        if (!File.Exists(file)) return gitDir;
        var p = File.ReadLines(file).FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(p)) return gitDir;
        return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(gitDir, p));
    }

    /// <summary>"aghouse" for C:\src\aghouse\.git, "aghouse" for a bare D:\repos\aghouse.git.</summary>
    private static string RepoName(string commonDir, string workingTree)
    {
        var trimmed = commonDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);
        if (leaf.Equals(".git", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileName(Path.GetDirectoryName(trimmed)) is { Length: > 0 } parent ? parent : Path.GetFileName(workingTree);
        if (leaf.EndsWith(".git", StringComparison.OrdinalIgnoreCase) && leaf.Length > 4) return leaf[..^4];
        // A submodule's git dir lives under the parent's .git/modules: it's named after its own folder.
        return Path.GetFileName(workingTree.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } own ? own : workingTree;
    }

    private static string? ReadBranch(string gitDir)
    {
        var headFile = Path.Combine(gitDir, "HEAD");
        if (!File.Exists(headFile)) return null;
        var head = File.ReadAllText(headFile).Trim();
        const string prefix = "ref: refs/heads/";
        if (head.StartsWith(prefix, StringComparison.Ordinal)) return head[prefix.Length..];
        if (head.StartsWith("ref: ", StringComparison.Ordinal)) return head["ref: ".Length..];
        return head.Length >= 7 ? head[..7] : head;
    }
}

/// <summary>
/// Which sidebar group a workspace belongs in, from its active pane's folder: the git repo (all of its
/// worktrees together), else the folder itself. <see cref="Key"/> is stable and case-insensitive (it's
/// saved for collapsed groups); <see cref="Label"/> is what the group header says.
/// </summary>
public readonly record struct ProjectKey(string Key, string Label)
{
    public static readonly ProjectKey Other = new("other", "Other");

    public static ProjectKey For(string? cwd, RepoInfo? repo, string? home = null)
    {
        if (repo is not null)
            return new("repo:" + Normalize(repo.CommonDir), repo.Name);
        if (string.IsNullOrWhiteSpace(cwd)) return Other;

        var dir = Normalize(cwd);
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var label = !string.IsNullOrEmpty(home) && dir == Normalize(home) ? "~"
            : Path.GetFileName(cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name ? name
            : cwd; // a drive root: "C:\"
        return new("dir:" + dir, label);
    }

    private static string Normalize(string path)
    {
        try { path = Path.GetFullPath(path); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // "C:\" stays "c:\" rather than "c:" (which means "the current folder on C:").
        return (trimmed.EndsWith(':') ? path : trimmed).ToLowerInvariant();
    }
}
