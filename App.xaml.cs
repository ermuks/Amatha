using System.Net;
using System.Threading;
using System.Windows;

namespace Amaranth10API;

public partial class App : Application
{
    private const string MutexName = @"Local\Amaranth10.아맛다보고서";
    private const string ActivateName = @"Local\Amaranth10.아맛다보고서.Activate";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;
    private CancellationTokenSource? _activateWatch;

    public static Services.AmaranthClient Client { get; } = new();

    public static bool StartedFromWindowsStartup { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        StartedFromWindowsStartup = e.Args.Any(arg =>
            string.Equals(arg, "--autostart", StringComparison.OrdinalIgnoreCase));

        if (!TryTakeSingleInstance())
        {
            if (!StartedFromWindowsStartup)
            {
                TryActivateExisting();
            }

            Shutdown();
            return;
        }

        StartActivateWatcher();
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        Helpers.WindowsNotification.Initialize();
        _ = Helpers.AppRuntime.Current;
        Helpers.StartupRegistration.Apply(Helpers.AppRuntime.Current.Settings.RunOnWindowsStartup);
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateWatch?.Cancel();
        _activateEvent?.Dispose();
        if (_instanceMutex != null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _instanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private bool TryTakeSingleInstance()
    {
        _instanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (createdNew)
        {
            return true;
        }

        try
        {
            if (_instanceMutex.WaitOne(0))
            {
                return true;
            }
        }
        catch (AbandonedMutexException)
        {
            return true;
        }

        _instanceMutex.Dispose();
        _instanceMutex = null;
        return false;
    }

    private void StartActivateWatcher()
    {
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);
        _activateWatch = new CancellationTokenSource();
        CancellationToken token = _activateWatch.Token;
        EventWaitHandle activateEvent = _activateEvent;
        Task.Run(() =>
        {
            WaitHandle[] handles = { activateEvent, token.WaitHandle };
            while (!token.IsCancellationRequested)
            {
                if (WaitHandle.WaitAny(handles) != 0)
                {
                    return;
                }

                Dispatcher.BeginInvoke(ActivateMainWindow);
            }
        }, token);
    }

    private static void TryActivateExisting()
    {
        try
        {
            using EventWaitHandle activate = EventWaitHandle.OpenExisting(ActivateName);
            activate.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is MainWindow window)
        {
            window.ShowFromTray();
        }
    }
}
