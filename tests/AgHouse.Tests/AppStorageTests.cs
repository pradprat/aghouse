using System.Text.Json;
using AgHouse.App.Persistence;
using AgHouse.Core.Layout;

// Stand-in for the App's Log. The real one creates %LOCALAPPDATA%\aghouse and appends to the real log file,
// which tests must never do. AppStorage.cs is compiled into this test assembly and resolves Log to this type.
namespace AgHouse.App
{
    public static class Log
    {
        private static readonly object Gate = new();
        private static readonly List<string> Seen = [];

        public static IReadOnlyList<string> Messages
        {
            get { lock (Gate) return Seen.ToList(); }
        }

        public static void Write(string message)
        {
            lock (Gate) Seen.Add(message);
        }
    }
}

namespace AgHouse.Tests
{
    /// <summary>
    /// Tests for the real AppStorage (settings.json and session.json), pointed at a private temp folder.
    /// AppStorage.Dir is static state, so this class lives in a collection of its own and restores Dir afterwards.
    /// Every write is preceded by <see cref="Guard"/>, which fails the test if Dir is not inside our temp folder.
    /// </summary>
    [Collection("AppStorage static state")]
    public sealed class AppStorageTests : IDisposable
    {
        private const string TempPrefix = "aghouse-test-appstorage-";

        private readonly string _originalDir = AppStorage.Dir;
        private readonly string _root;

        public AppStorageTests()
        {
            _root = Path.Combine(Path.GetFullPath(Path.GetTempPath()), TempPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            AppStorage.Dir = _root;
            Guard();
        }

        public void Dispose()
        {
            AppStorage.Dir = _originalDir;
            // Only ever delete our own temp folder.
            if (IsOurTempDir(_root) && Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private static string RealDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "aghouse");

        private bool IsOurTempDir(string path)
        {
            var full = Path.GetFullPath(path);
            var tempRoot = Path.GetFullPath(Path.GetTempPath());
            return full.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                   && Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)).StartsWith(TempPrefix, StringComparison.Ordinal);
        }

        /// <summary>Fails the test before any file is touched if Dir is not inside our own temp folder.</summary>
        private void Guard()
        {
            var dir = Path.GetFullPath(AppStorage.Dir);
            var inside = dir.StartsWith(_root, StringComparison.OrdinalIgnoreCase);
            var real = string.Equals(dir.TrimEnd('\\', '/'), Path.GetFullPath(RealDir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
            if (!inside || real)
                throw new InvalidOperationException($"AppStorage.Dir ('{dir}') is not inside the test temp folder; refusing to touch it.");
        }

        // ---- thin wrappers so no call reaches the disk without the guard ----
        private AppSettings LoadSettings(out SettingsProblem? problem) { Guard(); return AppStorage.LoadSettings(out problem); }
        private void EnsureSettingsFile() { Guard(); AppStorage.EnsureSettingsFile(); }
        private SessionDto? LoadSession() { Guard(); return AppStorage.LoadSession(); }
        private void SaveSession(SessionDto s) { Guard(); AppStorage.SaveSession(s); }

        private static string SettingsFile(string dir) => Path.Combine(dir, "settings.json");
        private static string SessionFile(string dir) => Path.Combine(dir, "session.json");

        private void WriteFile(string path, string text) { Guard(); File.WriteAllText(path, text); }

        private static SessionDto SampleSession()
        {
            var tree = new PaneTree("a");
            tree.Split("a", Direction.Right, "b", 0.3);
            return new SessionDto
            {
                SelectedIndex = 1,
                Workspaces =
                [
                    new WorkspaceDto
                    {
                        Name = "agents",
                        Layout = PaneLayoutSerializer.ToDto(tree.Root!, id => (id == "a" ? @"C:\one" : @"C:\two", "pwsh")),
                    },
                    new WorkspaceDto { Name = "solo", Layout = new PaneLayoutDto { Cwd = @"C:\solo" } },
                ],
            };
        }

        // ---------------- location ----------------

        [Fact]
        public void DefaultDir_IsRoamingAppData_Aghouse()
        {
            // _originalDir was captured before the constructor redirected Dir: this is what the app really uses.
            Assert.Equal(RealDir, _originalDir);
        }

        [Fact]
        public void Paths_FollowDir()
        {
            Assert.Equal(Path.Combine(_root, "settings.json"), AppStorage.SettingsPath);
            LoadSettings(out _);
            Assert.True(File.Exists(SettingsFile(_root)));
        }

        [Fact]
        public void Guard_RefusesTheRealFolder()
        {
            AppStorage.Dir = RealDir;
            Assert.Throws<InvalidOperationException>(() => LoadSettings(out _));
            Assert.Throws<InvalidOperationException>(() => SaveSession(new SessionDto()));
            Assert.Throws<InvalidOperationException>(() => EnsureSettingsFile());
            AppStorage.Dir = _root;
        }

        // ---------------- settings.json ----------------

        [Fact]
        public void Settings_Missing_IsCreatedWithDefaults()
        {
            var s = LoadSettings(out var problem);

            Assert.Null(problem);
            Assert.Equal("Cascadia Mono", s.FontFamily);
            Assert.Equal(11, s.FontSize);
            Assert.True(s.RestoreSession);
            Assert.True(s.Toasts);
            Assert.Equal("system", s.Theme);
            Assert.Null(s.DefaultProfile);

            var json = File.ReadAllText(SettingsFile(_root));
            Assert.Contains("\"fontFamily\": \"Cascadia Mono\"", json);
            Assert.Contains("\"fontSize\": 11", json);
            Assert.Contains("\"theme\": \"system\"", json);
            Assert.Contains("\"toasts\": true", json);
            Assert.Contains("\"restoreSession\": true", json);
        }

        [Fact]
        public void Settings_Valid_AreLoaded_AndTheFileIsLeftAlone()
        {
            const string text = """{ "theme": "dark", "fontSize": 14, "fontFamily": "Consolas", "toasts": false, "defaultProfile": "cmd", "restoreSession": false }""";
            WriteFile(SettingsFile(_root), text);

            var s = LoadSettings(out var problem);

            Assert.Null(problem);
            Assert.Equal("dark", s.Theme);
            Assert.Equal(14, s.FontSize);
            Assert.Equal("Consolas", s.FontFamily);
            Assert.False(s.Toasts);
            Assert.False(s.RestoreSession);
            Assert.Equal("cmd", s.DefaultProfile);
            Assert.Equal(text, File.ReadAllText(SettingsFile(_root)));
        }

        [Fact]
        public void Settings_Partial_FallsBackToDefaultsPerField()
        {
            WriteFile(SettingsFile(_root), """{ "theme": "light" }""");

            var s = LoadSettings(out var problem);

            Assert.Null(problem);
            Assert.Equal("light", s.Theme);
            Assert.Equal(11, s.FontSize);
            Assert.Equal("Cascadia Mono", s.FontFamily);
            Assert.True(s.Toasts);
        }

        [Fact]
        public void Settings_PropertyNames_AreCaseInsensitive_AndNumbersMayBeStrings()
        {
            WriteFile(SettingsFile(_root), """{ "Theme": "dark", "FONTSIZE": "13" }""");
            var s = LoadSettings(out var problem);
            Assert.Null(problem);
            Assert.Equal("dark", s.Theme);
            Assert.Equal(13, s.FontSize);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("null")]
        [InlineData("""{ "unknownSetting": 1 }""")]
        public void Settings_EmptyOrUnknownContent_GivesDefaultsWithoutAProblem(string text)
        {
            WriteFile(SettingsFile(_root), text);

            var s = LoadSettings(out var problem);

            Assert.Null(problem);
            Assert.Equal("system", s.Theme);
            Assert.Equal(text, File.ReadAllText(SettingsFile(_root)));
        }

        [Fact]
        public void Settings_SyntaxError_LeavesFileAsIs_UsesDefaults_AndReportsTheLine()
        {
            // The README: "aghouse leaves it as it is, runs on default settings, and shows the line with the error".
            // Line 3 is missing the comma after "dark"'s neighbour, so the parser reports the problem on line 3.
            const string text = "{\r\n  \"theme\": \"dark\"\r\n  \"fontSize\": 20\r\n}\r\n";
            WriteFile(SettingsFile(_root), text);
            var before = File.ReadAllBytes(SettingsFile(_root));
            var logged = AgHouse.App.Log.Messages.Count;

            var s = LoadSettings(out var problem);

            // defaults, not the half-readable values
            Assert.Equal("system", s.Theme);
            Assert.Equal(11, s.FontSize);

            Assert.NotNull(problem);
            Assert.Equal("settings.json has an error on line 3", problem!.Summary);
            Assert.False(string.IsNullOrWhiteSpace(problem.Detail));

            // untouched: same bytes, nothing else written next to it
            Assert.Equal(before, File.ReadAllBytes(SettingsFile(_root)));
            Assert.Equal([SettingsFile(_root)], Directory.GetFileSystemEntries(_root));

            // and the problem is recorded in the (stand-in) log
            Assert.Contains(AgHouse.App.Log.Messages.Skip(logged), m => m.StartsWith("settings: settings.json has an error on line 3"));
        }

        [Theory]
        [InlineData("{ \"theme\": \"dark\"", "settings.json has an error on line 1")]         // truncated
        [InlineData("{\n\n\n  oops\n}", "settings.json has an error on line 4")]                // bare word on line 4
        [InlineData("{ \"fontSize\": \"big\" }", "settings.json has an error on line 1")]       // wrong type
        [InlineData("{ \"toasts\": 5 }", "settings.json has an error on line 1")]               // wrong type
        [InlineData("[1, 2]", "settings.json has an error on line 1")]                          // wrong shape
        [InlineData("", "settings.json has an error")]                                          // empty file
        public void Settings_Broken_Variants_ReportAProblem_AndKeepTheFile(string text, string expectedSummary)
        {
            WriteFile(SettingsFile(_root), text);

            var s = LoadSettings(out var problem);

            Assert.NotNull(problem);
            Assert.StartsWith(expectedSummary, problem!.Summary);
            Assert.Equal("system", s.Theme);
            Assert.Equal(text, File.ReadAllText(SettingsFile(_root)));
        }

        [Fact]
        public void Settings_LockedFile_IsReportedAsUnreadable_AndKeptIntact()
        {
            // File locking with FileShare.None is only dependable on Windows (the only OS this app and its CI run on).
            if (!OperatingSystem.IsWindows()) return;

            const string text = """{ "theme": "dark" }""";
            WriteFile(SettingsFile(_root), text);

            SettingsProblem? problem;
            AppSettings s;
            using (new FileStream(SettingsFile(_root), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                s = LoadSettings(out problem);
            }

            Assert.NotNull(problem);
            Assert.Equal("settings.json couldn't be read", problem!.Summary);
            Assert.Equal("system", s.Theme);
            Assert.Equal(text, File.ReadAllText(SettingsFile(_root)));
        }

        [Fact]
        public void EnsureSettingsFile_CreatesWhenMissing()
        {
            EnsureSettingsFile();
            Assert.True(File.Exists(SettingsFile(_root)));
            Assert.Contains("\"theme\"", File.ReadAllText(SettingsFile(_root)));
        }

        [Theory]
        [InlineData("""{   "theme"  :  "dark"  }  """)]
        [InlineData("{ this is not json")]
        [InlineData("")]
        public void EnsureSettingsFile_NeverRewritesAnExistingFile(string text)
        {
            WriteFile(SettingsFile(_root), text);

            EnsureSettingsFile();

            Assert.Equal(text, File.ReadAllText(SettingsFile(_root)));
        }

        [Fact]
        public void Settings_CreatedFile_CanBeReadBack()
        {
            LoadSettings(out _);                         // creates the file
            var again = LoadSettings(out var problem);   // reads it

            Assert.Null(problem);
            Assert.Equal("Cascadia Mono", again.FontFamily);
            Assert.Equal(11, again.FontSize);
        }

        [Fact]
        public void Settings_Missing_InAFolderThatDoesNotExistYet_CreatesTheFolder()
        {
            AppStorage.Dir = Path.Combine(_root, "fresh", "nested");

            LoadSettings(out var problem);

            Assert.Null(problem);
            Assert.True(File.Exists(Path.Combine(_root, "fresh", "nested", "settings.json")));
        }

        // ---------------- session.json ----------------

        [Fact]
        public void Session_Missing_IsNull()
        {
            Assert.Null(LoadSession());
            Assert.False(File.Exists(SessionFile(_root)));
        }

        [Fact]
        public void Session_SaveThenLoad_RoundTrips()
        {
            SaveSession(SampleSession());

            var back = LoadSession()!;

            Assert.Equal(1, back.SelectedIndex);
            Assert.Equal(["agents", "solo"], back.Workspaces.Select(w => w.Name));
            var layout = back.Workspaces[0].Layout!;
            Assert.Equal("split", layout.Type);
            Assert.Equal(SplitOrientation.Columns, layout.Orientation);
            Assert.Equal(0.3, layout.Ratio, 6);
            Assert.Equal(@"C:\one", layout.First!.Cwd);
            Assert.Equal("pwsh", layout.First.Profile);
            Assert.Equal(@"C:\solo", back.Workspaces[1].Layout!.Cwd);
        }

        [Fact]
        public void Session_File_UsesCamelCase_AndStringEnums()
        {
            SaveSession(SampleSession());
            var json = File.ReadAllText(SessionFile(_root));
            Assert.Contains("\"selectedIndex\": 1", json);
            Assert.Contains("\"orientation\": \"columns\"", json);
            Assert.Contains("\"workspaces\"", json);
        }

        [Fact]
        public void Session_Save_LeavesNoTempFileBehind()
        {
            SaveSession(SampleSession());
            Assert.Equal([SessionFile(_root)], Directory.GetFileSystemEntries(_root));
        }

        [Fact]
        public void Session_SaveTwice_ReplacesTheOldContent()
        {
            SaveSession(SampleSession());
            SaveSession(new SessionDto { SelectedIndex = 0, Workspaces = [new WorkspaceDto { Name = "only" }] });

            var back = LoadSession()!;

            Assert.Equal(["only"], back.Workspaces.Select(w => w.Name));
            Assert.Single(Directory.GetFileSystemEntries(_root));
        }

        [Fact]
        public void Session_Save_CreatesAMissingFolder()
        {
            AppStorage.Dir = Path.Combine(_root, "a", "b");

            SaveSession(new SessionDto());

            Assert.True(File.Exists(Path.Combine(_root, "a", "b", "session.json")));
        }

        [Theory]
        [InlineData("{ truncated")]
        [InlineData("")]
        [InlineData("null")]
        [InlineData("""{"workspaces":"not a list"}""")]
        [InlineData("""{"workspaces":[{"layout":{"orientation":"diagonal"}}]}""")]
        public void Session_Corrupt_IsNull_NotAnException_AndTheFileIsKept(string text)
        {
            WriteFile(SessionFile(_root), text);

            Assert.Null(LoadSession());
            Assert.Equal(text, File.ReadAllText(SessionFile(_root)));
        }

        [Fact]
        public void Session_LockedFile_IsNull()
        {
            if (!OperatingSystem.IsWindows()) return;

            SaveSession(SampleSession());
            SessionDto? loaded;
            using (new FileStream(SessionFile(_root), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                loaded = LoadSession();
            }

            Assert.Null(loaded);
            Assert.NotNull(LoadSession()); // once unlocked it loads again
        }

        [Fact]
        public void Session_Save_WhenTheFolderCannotBeCreated_DoesNotThrow()
        {
            // The "folder" lies below a regular file, so Directory.CreateDirectory fails.
            var blocker = Path.Combine(_root, "blocker");
            WriteFile(blocker, "I am a file");
            AppStorage.Dir = Path.Combine(blocker, "sub");

            var ex = Record.Exception(() => SaveSession(SampleSession()));

            Assert.Null(ex);
            Assert.Null(LoadSession());
            Assert.Equal("I am a file", File.ReadAllText(blocker));
        }

        [Fact]
        public void Session_FileIsValidJson()
        {
            SaveSession(SampleSession());
            using var doc = JsonDocument.Parse(File.ReadAllText(SessionFile(_root)));
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        }
    }
}
