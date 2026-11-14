using System.Collections;
using AgHouse.Core.Terminal;

namespace AgHouse.Tests;

public class InheritedEnvTests
{
    [Fact]
    public void DropsClaudeSessionMarkersAndTheirNoColor()
    {
        var env = new Hashtable
        {
            ["CLAUDECODE"] = "1",
            ["CLAUDE_CODE_CHILD_SESSION"] = "1",
            ["CLAUDE_CODE_SESSION_ID"] = "abc",
            ["AI_AGENT"] = "claude-code",
            ["NO_COLOR"] = "1",
            ["PATH"] = @"C:\bin",
        };

        var unset = InheritedEnv.ToUnset(env);

        Assert.Equal(
            new[] { "AI_AGENT", "CLAUDECODE", "CLAUDE_CODE_CHILD_SESSION", "CLAUDE_CODE_SESSION_ID", "NO_COLOR" },
            unset.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void KeepsNoColorTheUserSetThemselves()
    {
        var env = new Hashtable { ["NO_COLOR"] = "1", ["PATH"] = @"C:\bin" };
        Assert.Empty(InheritedEnv.ToUnset(env));
    }

    [Fact]
    public void KeepsClaudeCodeUserSettings()
    {
        var env = new Hashtable { ["CLAUDECODE"] = "1", ["CLAUDE_CODE_USE_BEDROCK"] = "1", ["CLAUDE_CODE_GIT_BASH_PATH"] = @"C:\git\bash.exe" };
        Assert.Equal(new[] { "CLAUDECODE" }, InheritedEnv.ToUnset(env));
    }
}
