using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Threading;
using Amaranth10API.Models;
using Amaranth10API.ViewModels;

namespace Amaranth10API.Helpers;

public sealed class AppRuntime
{
    public static AppRuntime Current { get; } = new();

    private DispatcherTimer _refreshTimer = new();
    private bool _wasOnline = NetworkInterface.GetIsNetworkAvailable();
    private bool _reloginInProgress;

    private AppRuntime()
    {
        Settings = SettingsStore.Load();
        Settings.PropertyChanged += SettingsOnPropertyChanged;
        _refreshTimer.Tick += RefreshTimerOnTick;
        NetworkChange.NetworkAvailabilityChanged += NetworkAvailabilityChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        SettingsStore.Save(Settings);
        if (e.PropertyName is nameof(AppSettings.NotificationIntervalSeconds)
            or nameof(AppSettings.NotificationsEnabled)
            or null)
        {
            RestartRefreshTimer();
        }

        if (e.PropertyName is nameof(AppSettings.RunOnWindowsStartup) or null)
        {
            StartupRegistration.Apply(Settings.RunOnWindowsStartup);
        }

        if (e.PropertyName is nameof(AppSettings.AutoLoginEnabled))
        {
            SyncSavedLogin();
        }
    }

    private async void RefreshTimerOnTick(object? sender, EventArgs e)
    {
        await RefreshQuietAsync();
    }

    public AppSettings Settings { get; }

    public string LoginId { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public AmaranthSession? Session { get; set; }

    public DashboardViewModel? Dashboard { get; set; }

    public bool IsLoggedIn => Session != null;

    public void RememberLogin(string loginId, string password, AmaranthSession session)
    {
        LoginId = loginId;
        Password = password;
        Session = session;
        SyncSavedLogin();
        RestartRefreshTimer();
    }

    public void ClearLogin()
    {
        Session = null;
        Dashboard = null;
        LoginId = string.Empty;
        Password = string.Empty;
        _refreshTimer.Stop();
    }

    public void ForgetSavedLogin()
    {
        CredentialStore.Delete();
        ClearLogin();
    }

    public void SyncSavedLogin()
    {
        if (Settings.AutoLoginEnabled &&
            !string.IsNullOrWhiteSpace(LoginId) &&
            !string.IsNullOrWhiteSpace(Password))
        {
            CredentialStore.Save(LoginId, Password);
            return;
        }

        CredentialStore.Delete();
    }

    public async Task<bool> TryRestoreSessionAsync()
    {
        if (!Settings.AutoLoginEnabled)
        {
            return false;
        }

        SavedLogin? saved = CredentialStore.Load();
        if (saved == null)
        {
            return false;
        }

        LoginId = saved.LoginId;
        Password = saved.Password;
        int attempts = App.StartedFromWindowsStartup ? 12 : 3;
        int delaySeconds = App.StartedFromWindowsStartup ? 10 : 2;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }

            try
            {
                Session = await App.Client.LoginAsync(LoginId, Password);
                RestartRefreshTimer();
                return true;
            }
            catch
            {
                // 부팅 직후 네트워크가 아직 없으면 잠시 후 다시 시도합니다.
            }
        }

        return false;
    }

    public void RestartRefreshTimer()
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimerOnTick;

        if (!IsLoggedIn || !Settings.NotificationsEnabled)
        {
            return;
        }

        int seconds = Math.Max(10, Settings.NotificationIntervalSeconds);
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(seconds)
        };
        _refreshTimer.Tick += RefreshTimerOnTick;
        _refreshTimer.Start();
    }

    private bool _refreshInProgress;

    public async Task RefreshQuietAsync()
    {
        if (Dashboard == null || !IsLoggedIn || _refreshInProgress)
        {
            return;
        }

        _refreshInProgress = true;
        try
        {
            await Dashboard.ReloadAsync(silent: true);
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private async void NetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
    {
        bool nowOnline = e.IsAvailable;
        bool reconnected = nowOnline && !_wasOnline;
        _wasOnline = nowOnline;

        if (!reconnected || !Settings.KeepSessionOnReconnect || _reloginInProgress || !IsLoggedIn)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(LoginId) || string.IsNullOrWhiteSpace(Password))
        {
            return;
        }

        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        await dispatcher.InvokeAsync(ReloginAsync);
    }

    private async Task ReloginAsync()
    {
        if (_reloginInProgress)
        {
            return;
        }

        _reloginInProgress = true;
        try
        {
            AmaranthSession session = await App.Client.LoginAsync(LoginId, Password);
            Session = session;
            if (Dashboard != null)
            {
                await Dashboard.InitializeAsync(session);
            }
        }
        catch
        {
            // 재연결 로그인 실패 시 다음 주기에 다시 시도합니다.
        }
        finally
        {
            _reloginInProgress = false;
        }
    }
}
