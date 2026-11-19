using System.Windows;
using AgHouse.App.Ipc;
using AgHouse.App.Notifications;
using AgHouse.App.Persistence;
using AgHouse.App.Terminal;
using AgHouse.Core.Ipc;

namespace AgHouse.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private PipeServer? _pipe;
    private Updates.UpdateService? _updates;
    private bool _sessionEnding;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\" + AgEnv.DefaultPipeName, out var isFirst);
        if (!isFirst)
        {
            ActivateRunningInstance();
            Shutdown();
            return;
        }

        var settings = AppStorage.LoadSettings(out var settingsProblem);
        ThemeMode = settings.Theme switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        Theming.Typography.Start(this);

        var profiles = new ShellProfiles(settings);
        var toasts = new ToastService();
        var window = new MainWindow(settings, settingsProblem, profiles, toasts);
        MainWindow = window;

        _pipe = new PipeServer(AgEnv.DefaultPipeName, window.Controller.HandleRpcAsync);
        _pipe.Start();

        window.Show();
        _ = profiles.DetectWslAsync();
        _updates = window.Controller.Updates;
        _updates.Start();
    }

    private static void ActivateRunningInstance()
    {
        try
        {
            Task.Run(async () =>
            {
                await using var client = await RpcClient.ConnectAsync(AgEnv.DefaultPipeName, 2000);
                await client.CallAsync(Methods.Activate);
            }).Wait(3000);
        }
        catch (AggregateException) { /* the other instance is still starting or hung */ }
    }

    /// <summary>
    /// Windows is signing out or shutting down. Window.Closing doesn't run on this path, so save the
    /// session here, and don't ask about running programs: Windows is ending them either way.
    /// </summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        _sessionEnding = true;
        (MainWindow as MainWindow)?.Controller.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipe?.Dispose();
        _singleInstance?.Dispose();
        // Last: the installer replaces these files, and with /relaunch=1 starts a new aghouse that
        // needs the pipe and the single-instance mutex to be free.
        _updates?.RunPendingInstall(_sessionEnding);
        base.OnExit(e);
    }
}
