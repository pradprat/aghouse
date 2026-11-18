using AgHouse.Core;
using AgHouse.Core.Ipc;

namespace AgHouse.Tests;

public class GitInfoTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aghouse-git").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Branch_FromNestedDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        File.WriteAllText(Path.Combine(_root, ".git", "HEAD"), "ref: refs/heads/feature/x\n");
        var nested = Directory.CreateDirectory(Path.Combine(_root, "src", "deep")).FullName;
        Assert.Equal("feature/x", GitInfo.TryGetBranch(nested));
    }

    [Fact]
    public void DetachedHead_ShortSha()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        File.WriteAllText(Path.Combine(_root, ".git", "HEAD"), "0123456789abcdef\n");
        Assert.Equal("0123456", GitInfo.TryGetBranch(_root));
    }

    [Fact]
    public void Worktree_GitFile()
    {
        var real = Directory.CreateDirectory(Path.Combine(_root, "realgit")).FullName;
        File.WriteAllText(Path.Combine(real, "HEAD"), "ref: refs/heads/wt\n");
        var wt = Directory.CreateDirectory(Path.Combine(_root, "wt")).FullName;
        File.WriteAllText(Path.Combine(wt, ".git"), "gitdir: ../realgit\n");
        Assert.Equal("wt", GitInfo.TryGetBranch(wt));
    }

    [Fact]
    public void NotARepo_Null()
    {
        Assert.Null(GitInfo.TryGetBranch(Path.Combine(_root, "missing")));
    }

    /// <summary>C:\…\&lt;name&gt; with a .git folder on branch main.</summary>
    private string MakeRepo(string name)
    {
        var repo = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        File.WriteAllText(Path.Combine(repo, ".git", "HEAD"), "ref: refs/heads/main\n");
        return repo;
    }

    [Fact]
    public void Repo_NamedAfterItsFolder_FromANestedFolder()
    {
        var repo = MakeRepo("shop");
        var nested = Directory.CreateDirectory(Path.Combine(repo, "src", "api")).FullName;

        var info = GitInfo.TryGetRepo(nested)!;

        Assert.Equal("shop", info.Name);
        Assert.Equal(repo, info.Root);
        Assert.Equal(Path.Combine(repo, ".git"), info.CommonDir);
        Assert.Equal("main", info.Branch);
    }

    [Fact]
    public void Repo_AWorktreeIsNamedAfterItsMainRepo()
    {
        // git worktree add ../shop-fix: <wt>/.git → <repo>/.git/worktrees/shop-fix, whose commondir is ../..
        var repo = MakeRepo("shop");
        var wtGit = Directory.CreateDirectory(Path.Combine(repo, ".git", "worktrees", "shop-fix")).FullName;
        File.WriteAllText(Path.Combine(wtGit, "HEAD"), "ref: refs/heads/fix\n");
        File.WriteAllText(Path.Combine(wtGit, "commondir"), "../..\n");
        var wt = Directory.CreateDirectory(Path.Combine(_root, "shop-fix")).FullName;
        File.WriteAllText(Path.Combine(wt, ".git"), $"gitdir: {wtGit}\n");

        var info = GitInfo.TryGetRepo(wt)!;
        var main = GitInfo.TryGetRepo(repo)!;

        Assert.Equal("shop", info.Name);
        Assert.Equal("fix", info.Branch);
        Assert.Equal(wt, info.Root);
        Assert.Equal(main.CommonDir, info.CommonDir);
        Assert.Equal(ProjectKey.For(repo, main), ProjectKey.For(wt, info));
    }

    [Fact]
    public void Repo_ASubmoduleIsNamedAfterItsOwnFolder()
    {
        var parent = MakeRepo("app");
        var modGit = Directory.CreateDirectory(Path.Combine(parent, ".git", "modules", "lib")).FullName;
        File.WriteAllText(Path.Combine(modGit, "HEAD"), "0123456789abcdef\n");
        var lib = Directory.CreateDirectory(Path.Combine(parent, "vendor", "lib")).FullName;
        File.WriteAllText(Path.Combine(lib, ".git"), "gitdir: ../../.git/modules/lib\n");

        var info = GitInfo.TryGetRepo(lib)!;

        Assert.Equal("lib", info.Name);
        Assert.Equal("0123456", info.Branch);
        Assert.NotEqual(ProjectKey.For(parent, GitInfo.TryGetRepo(parent)).Key, ProjectKey.For(lib, info).Key);
    }

    [Fact]
    public void Repo_NullOutsideARepoOrForAMissingFolder()
    {
        Assert.Null(GitInfo.TryGetRepo(Directory.CreateDirectory(Path.Combine(_root, "plain")).FullName));
        Assert.Null(GitInfo.TryGetRepo(Path.Combine(_root, "missing")));
        Assert.Null(GitInfo.TryGetRepo(null));
    }
}

public class ProjectKeyTests
{
    [Fact]
    public void AFolderOutsideARepo_IsItsOwnGroup_WhateverTheCase()
    {
        var a = ProjectKey.For(@"C:\Work\Notes", null, home: @"C:\Users\me");
        var b = ProjectKey.For(@"c:\work\notes\", null, home: @"C:\Users\me");
        Assert.Equal("Notes", a.Label);
        Assert.Equal(a.Key, b.Key);
        Assert.StartsWith("dir:", a.Key);
    }

    [Fact]
    public void TheHomeFolder_IsTilde() =>
        Assert.Equal("~", ProjectKey.For(@"C:\Users\Me\", null, home: @"C:\Users\me").Label);

    [Fact]
    public void ADriveRoot_KeepsItsName() =>
        Assert.Equal(@"D:\", ProjectKey.For(@"D:\", null, home: @"C:\Users\me").Label);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoFolder_IsOther(string? cwd) => Assert.Equal(ProjectKey.Other, ProjectKey.For(cwd, null));

    [Fact]
    public void ARepo_IsKeyedByItsSharedGitDir_AndNamedForTheRepo()
    {
        var repo = new RepoInfo(@"C:\src\shop-fix", @"C:\src\Shop\.git", "Shop", "fix");
        var key = ProjectKey.For(@"C:\src\shop-fix\api", repo);
        Assert.Equal("Shop", key.Label);
        Assert.Equal(@"repo:c:\src\shop\.git", key.Key);
    }
}

public class ProtocolTests
{
    [Fact]
    public void Request_RoundTrip()
    {
        var req = new RpcRequest { Id = 7, Method = Methods.Notify, Params = { ["title"] = "T", ["body"] = null } };
        var back = RpcJson.Deserialize<RpcRequest>(RpcJson.Serialize(req))!;
        Assert.Equal(7, back.Id);
        Assert.Equal("notify", back.Method);
        Assert.Equal("T", back.Get("title"));
        Assert.Null(back.Get("body"));
        Assert.DoesNotContain('\n', RpcJson.Serialize(req));
    }

    [Fact]
    public void Response_RoundTrip()
    {
        var res = RpcResponse.Success(3, new System.Text.Json.Nodes.JsonObject { ["paneId"] = "p1" });
        var back = RpcJson.Deserialize<RpcResponse>(RpcJson.Serialize(res))!;
        Assert.True(back.Ok);
        Assert.Equal("p1", back.Result!["paneId"]!.GetValue<string>());
    }

    [Fact]
    public void PipeName_IsSanitized()
    {
        Assert.Matches("^aghouse-[a-z0-9]+$", AgEnv.DefaultPipeName);
    }
}
