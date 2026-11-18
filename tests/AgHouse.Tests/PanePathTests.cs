using AgHouse.Core.Terminal;

namespace AgHouse.Tests;

public class PanePathTests
{
    [Fact]
    public void KeepsAFreshRegistryEntryTheLauncherNeverHad()
    {
        // The bug: aghouse started before nvm added its shim directory, so the process copy has the
        // old nvm entry only. A pane must still get the directory PATH holds now.
        var stale = @"C:\Windows;C:\Users\me\AppData\Local\nvm\\.nodejs";
        var user = @"C:\Users\me\AppData\Local\nvm;C:\Users\me\AppData\Local\nvm\.shim;C:\Users\me\AppData\Local\nvm\\.nodejs";

        var path = PanePath.Compose(@"C:\Apps\aghouse", @"C:\Windows", user, stale);

        Assert.Equal(
            @"C:\Apps\aghouse;C:\Windows;C:\Users\me\AppData\Local\nvm;C:\Users\me\AppData\Local\nvm\.shim;C:\Users\me\AppData\Local\nvm\\.nodejs",
            path);
    }

    [Fact]
    public void LeavesNoEmptyEntryForTheCurrentDirectory()
    {
        Assert.Equal(@"C:\bin;C:\tools", PanePath.Compose(@";C:\bin", null, "", "  ", @";;C:\tools;;"));
    }

    [Fact]
    public void KeepsTheFirstCopyAndIgnoresCase()
    {
        Assert.Equal(@"C:\Tools;C:\bin", PanePath.Compose(@"C:\Tools;C:\bin", @"c:\tools;C:\BIN"));
    }

    [Fact]
    public void TrimsQuotesAndTrailingSeparatorsWithoutEatingDriveRoots()
    {
        Assert.Equal(@"C:\Program Files\Git\cmd;C:\Windows;C:\", PanePath.Compose(@"""C:\Program Files\Git\cmd\"";C:\Windows\\;C:\"));
    }

    [Fact]
    public void PutsTheAppDirectoryFirst()
    {
        var path = PanePath.ForPane(@"C:\Apps\aghouse\");
        Assert.StartsWith(@"C:\Apps\aghouse;", path);
    }
}
