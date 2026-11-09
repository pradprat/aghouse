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

        var profiles = new ShellProfiles(settings);
        var toasts = new ToastService();
        var window = new MainWindow(settings, settingsProblem, profiles, toasts);
        MainWindow = window;

        _pipe = new PipeServer(AgEnv.DefaultPipeName, window.Controller.HandleRpcAsync);
        _pipe.Start();

        window.Show();
        _ = profiles.DetectWslAsync();
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

    protected override void OnExit(ExitEventArgs e)
    {
        _pipe?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
