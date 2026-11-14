using System.Reflection;
using System.Text.Json.Nodes;
using AgHouse.Core.Ipc;
using AgHouse.Core.Settings;

namespace AgHouse.Tests;

public class SettingKeysTests
{
    [Theory]
    [InlineData("fontSize")]
    [InlineData("font-size")]
    [InlineData("font_size")]
    [InlineData("FONTSIZE")]
    [InlineData(" fontsize ")]
    public void Find_AcceptsAnySpelling(string name)
    {
        Assert.Equal("fontSize", SettingKeys.Find(name)?.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("colour")]
    public void Unknown_IsNull_AndRequireListsTheKeys(string? name)
    {
        Assert.Null(SettingKeys.Find(name));
        var ex = Assert.Throws<ArgumentException>(() => SettingKeys.Require(name));
        Assert.Contains("theme", ex.Message);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("ON", true)]
    [InlineData("yes", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    [InlineData(" No ", false)]
    [InlineData("0", false)]
    public void Bool_Spellings(string raw, bool expected)
    {
        Assert.Equal(expected, SettingKeys.Parse(SettingKeys.Require("toasts"), raw)!.GetValue<bool>());
    }

    [Fact]
    public void Bool_Rejects_Other()
    {
        Assert.Throws<ArgumentException>(() => SettingKeys.Parse(SettingKeys.Require("toasts"), "maybe"));
    }

    [Theory]
    [InlineData("light", "light")]
    [InlineData("DARK", "dark")]
    public void Theme_IsOneOfTheChoices(string raw, string expected)
    {
        Assert.Equal(expected, SettingKeys.Parse(SettingKeys.Require("theme"), raw)!.GetValue<string>());
    }

    [Fact]
    public void Theme_Rejects_Other()
    {
        var ex = Assert.Throws<ArgumentException>(() => SettingKeys.Parse(SettingKeys.Require("theme"), "blue"));
        Assert.Contains("system", ex.Message);
    }

    [Theory]
    [InlineData("6", 6)]
    [InlineData("14", 14)]
    [InlineData("48", 48)]
    public void FontSize_InRange(string raw, int expected)
    {
        Assert.Equal(expected, SettingKeys.Parse(SettingKeys.Require("fontSize"), raw)!.GetValue<int>());
    }

    [Theory]
    [InlineData("5")]
    [InlineData("49")]
    [InlineData("12.5")]
    [InlineData("big")]
    public void FontSize_Rejects(string raw)
    {
        Assert.Throws<ArgumentException>(() => SettingKeys.Parse(SettingKeys.Require("fontSize"), raw));
    }

    [Fact]
    public void Text_IsTrimmed_AndCantBeEmpty()
    {
        var key = SettingKeys.Require("fontFamily");
        Assert.Equal("Consolas", SettingKeys.Parse(key, "  Consolas ")!.GetValue<string>());
        Assert.Throws<ArgumentException>(() => SettingKeys.Parse(key, " "));
    }

    [Fact]
    public void Defaults_AreValidValues()
    {
        foreach (var key in SettingKeys.All.Where(k => k.Default is not null))
        {
            var raw = key.Default is JsonValue v && v.TryGetValue<string>(out var s) ? s : key.Default!.ToJsonString();
            Assert.Equal(key.Default!.ToJsonString(), SettingKeys.Parse(key, raw)!.ToJsonString());
        }
    }

    [Fact]
    public void Names_AreUnique()
    {
        Assert.Equal(SettingKeys.All.Count, SettingKeys.All.Select(k => k.Name.ToLowerInvariant()).Distinct().Count());
    }
}

public class ActionNamesTests
{
    [Fact]
    public void Names_AreUnique_AndKebabCase()
    {
        var names = ActionNames.All.Select(a => a.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(names, n => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", n));
    }

    [Fact]
    public void Find_IgnoresCase()
    {
        Assert.Equal("split-right", ActionNames.Find("Split-Right")?.Name);
        Assert.Null(ActionNames.Find("explode"));
    }

    [Fact]
    public void AllNineWorkspaces_CanBeSelected()
    {
        Assert.All(Enumerable.Range(1, 9), i => Assert.NotNull(ActionNames.Find($"select-workspace-{i}")));
    }

    [Fact]
    public void MethodNames_AreUnique()
    {
        var values = typeof(Methods).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
    }
}
