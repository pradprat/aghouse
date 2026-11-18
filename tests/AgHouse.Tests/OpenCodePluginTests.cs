using AgHouse.Core;
using Xunit;

namespace AgHouse.Tests;

public sealed class OpenCodePluginTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aghouse-oc-" + Guid.NewGuid().ToString("N"), "aghouse");
    private const string Exe = "C:/Program Files/aghouse/aghouse.exe";

    public void Dispose()
    {
        var parent = Path.GetDirectoryName(_dir)!;
        if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
    }

    [Fact]
    public void Script_fills_in_the_cli_and_version()
    {
        var js = OpenCodePlugin.Script(Exe);
        Assert.Contains("const AGHOUSE = \"C:/Program Files/aghouse/aghouse.exe\";", js);
        Assert.Contains($"(opencode-plugin {OpenCodePlugin.Version})", js);
        Assert.DoesNotContain("__AGHOUSE__", js);
        Assert.DoesNotContain("__VERSION__", js);
    }

    [Fact]
    public void A_path_with_quotes_or_backslashes_stays_one_js_string()
    {
        var js = OpenCodePlugin.Script("C:\\odd \"name\"\\aghouse.exe");
        Assert.Contains("const AGHOUSE = \"C:\\\\odd \\u0022name\\u0022\\\\aghouse.exe\";", js);
    }

    [Fact]
    public void Missing_folder_is_not_connected() =>
        Assert.Equal(HookState.NotConnected, OpenCodePlugin.Inspect(_dir, Exe).State);

    [Fact]
    public void Install_writes_the_folder_and_then_reads_as_connected()
    {
        Assert.True(OpenCodePlugin.Install(_dir, Exe));
        Assert.True(File.Exists(Path.Combine(_dir, "tui.js")));
        Assert.True(File.Exists(Path.Combine(_dir, "package.json")));
        Assert.Equal(HookState.Connected, OpenCodePlugin.Inspect(_dir, Exe).State);
        Assert.False(OpenCodePlugin.Install(_dir, Exe)); // nothing to do
    }

    [Fact]
    public void Another_copys_plugin_is_partial_and_names_that_copy()
    {
        OpenCodePlugin.Install(_dir, "D:/other/aghouse.exe");
        var status = OpenCodePlugin.Inspect(_dir, Exe);
        Assert.Equal(HookState.Partial, status.State);
        Assert.Equal("D:/other/aghouse.exe", status.OtherCopy);
        Assert.True(status.Reaches);
        Assert.True(OpenCodePlugin.Install(_dir, Exe));
        Assert.Equal(HookState.Connected, OpenCodePlugin.Inspect(_dir, Exe).State);
    }

    [Fact]
    public void An_older_version_is_partial_without_another_copy()
    {
        OpenCodePlugin.Install(_dir, Exe);
        var path = Path.Combine(_dir, "tui.js");
        File.WriteAllText(path, File.ReadAllText(path).Replace($"(opencode-plugin {OpenCodePlugin.Version})", "(opencode-plugin 0)"));
        var status = OpenCodePlugin.Inspect(_dir, Exe);
        Assert.Equal(HookState.Partial, status.State);
        Assert.Null(status.OtherCopy);
    }

    [Fact]
    public void Someone_elses_files_are_left_alone()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "tui.js"), "export default {}");
        Assert.Equal(HookState.Unreadable, OpenCodePlugin.Inspect(_dir, Exe).State);
        Assert.Throws<InvalidOperationException>(() => OpenCodePlugin.Install(_dir, Exe));
        Assert.Equal("export default {}", File.ReadAllText(Path.Combine(_dir, "tui.js")));
    }
}
