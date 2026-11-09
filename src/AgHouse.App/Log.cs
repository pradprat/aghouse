using System.IO;

namespace AgHouse.App;

/// <summary>Tiny append-only diagnostics log: %LOCALAPPDATA%\aghouse\aghouse.log</summary>
public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 1024 * 1024;

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "aghouse", "aghouse.log");

    static Log()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes) File.Delete(FilePath);
        }
        catch (IOException) { }
    }

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}";
        lock (Gate)
        {
            try { File.AppendAllText(FilePath, line); }
            catch (IOException) { }
        }
    }
}
