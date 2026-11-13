using System.Text.Json.Nodes;
using AgHouse.Core;

namespace AgHouse.Tests;

public class ClaudeHooksTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("aghouse-hooks").FullName;
    private string SettingsPath => Path.Combine(_dir, ".claude", "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void MissingFile_IsNotConnected_AndInstallCreatesIt()
    {
        Assert.Equal(HookState.NotConnected, ClaudeHooks.Inspect(SettingsPath).State);
        var (added, backup) = ClaudeHooks.Install(SettingsPath);
        Assert.Equal(ClaudeHooks.Events, added);
        Assert.Null(backup);
        Assert.Equal(HookState.Connected, ClaudeHooks.Inspect(SettingsPath).State);
    }

    [Fact]
    public void Install_KeepsOtherSettingsAndHooks_AndBacksUp()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """
            {
              "model": "opus",
              // a comment
              "hooks": {
                "Stop": [ { "hooks": [ { "type": "command", "command": "say done" } ] } ]
              }
            }
            """);
        var (added, backup) = ClaudeHooks.Install(SettingsPath);

        Assert.Equal(4, added.Count);
        Assert.True(File.Exists(backup));
        var root = JsonNode.Parse(File.ReadAllText(SettingsPath))!.AsObject();
        Assert.Equal("opus", root["model"]!.GetValue<string>());
        var stop = root["hooks"]!["Stop"]!.AsArray();
        Assert.Equal(2, stop.Count);
        Assert.Equal("say done", stop[0]!["hooks"]![0]!["command"]!.GetValue<string>());
        Assert.Equal(ClaudeHooks.Command, stop[1]!["hooks"]![0]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void Install_IsIdempotent()
    {
        ClaudeHooks.Install(SettingsPath);
        var before = File.ReadAllText(SettingsPath);
        var (added, _) = ClaudeHooks.Install(SettingsPath);
        Assert.Empty(added);
        Assert.Equal(before, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Partial_ListsWhatsMissing()
    {
        var root = JsonNode.Parse("""
            { "hooks": { "Notification": [ { "hooks": [ { "type": "command", "command": "aghouse claude-hook" } ] } ] } }
            """)!.AsObject();
        var status = ClaudeHooks.Inspect(root);
        Assert.Equal(HookState.Partial, status.State);
        Assert.Equal(["Stop", "UserPromptSubmit", "PostToolUse"], status.Missing);
    }

    [Fact]
    public void BrokenJson_IsUnreadable_AndNeverOverwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ not json");
        Assert.Equal(HookState.Unreadable, ClaudeHooks.Inspect(SettingsPath).State);
        Assert.ThrowsAny<Exception>(() => ClaudeHooks.Install(SettingsPath));
        Assert.Equal("{ not json", File.ReadAllText(SettingsPath));
    }
}
