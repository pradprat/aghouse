using EasyWindowsTerminalControl.Internals;

namespace AgHouse.App.Terminal;

/// <summary>
/// Starts the pane's shell with extra environment variables (AGHOUSE_PANE_ID, PATH, ...).
/// The library's factory passes a null environment block, so the child inherits ours:
/// we set the variables on this process just for the CreateProcess call, under a lock.
/// </summary>
internal sealed class EnvProcessFactory(IReadOnlyDictionary<string, string?> env) : IProcessFactory
{
    private static readonly object Gate = new();

    public event Action<IProcess>? ProcessStarted;

    public IProcess Start(string command, UIntPtr attributes, PseudoConsole console, string workingDirectory)
    {
        IProcess process;
        lock (Gate)
        {
            var saved = new Dictionary<string, string?>(env.Count);
            foreach (var (k, v) in env)
            {
                saved[k] = Environment.GetEnvironmentVariable(k);
                Environment.SetEnvironmentVariable(k, v);
            }
            try
            {
                process = ProcessFactory.Start(command, attributes, console, workingDirectory);
            }
            finally
            {
                foreach (var (k, v) in saved) Environment.SetEnvironmentVariable(k, v);
            }
        }
        ProcessStarted?.Invoke(process);
        return process;
    }
}
