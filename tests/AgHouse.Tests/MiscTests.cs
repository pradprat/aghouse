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
