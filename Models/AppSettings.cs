using Amaranth10API.ViewModels;

namespace Amaranth10API.Models;

public sealed class AppSettings : ObservableObject
{
    private bool _notificationsEnabled;
    private int _notificationIntervalSeconds = 3600;
    private bool _showGuiOnNotification;
    private bool _keepSessionOnReconnect;
    private bool _autoLoginEnabled;
    private bool _startInTray;
    private bool _runOnWindowsStartup = true;
    private int _applicationMonthsBefore = 3;
    private int _applicationMonthsAfter = 1;
    private int _reportMonthsBefore = 3;
    private int _reportMonthsAfter = 1;

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set
        {
            if (SetProperty(ref _notificationsEnabled, value))
            {
                OnPropertyChanged(nameof(NotificationOptionsEnabled));
            }
        }
    }

    public bool NotificationOptionsEnabled => NotificationsEnabled;

    public int NotificationIntervalSeconds
    {
        get => _notificationIntervalSeconds;
        set => SetProperty(ref _notificationIntervalSeconds, Math.Max(1, value));
    }

    public bool ShowGuiOnNotification
    {
        get => _showGuiOnNotification;
        set => SetProperty(ref _showGuiOnNotification, value);
    }

    public bool KeepSessionOnReconnect
    {
        get => _keepSessionOnReconnect;
        set => SetProperty(ref _keepSessionOnReconnect, value);
    }

    public bool AutoLoginEnabled
    {
        get => _autoLoginEnabled;
        set => SetProperty(ref _autoLoginEnabled, value);
    }

    public bool StartInTray
    {
        get => _startInTray;
        set => SetProperty(ref _startInTray, value);
    }

    public bool RunOnWindowsStartup
    {
        get => _runOnWindowsStartup;
        set => SetProperty(ref _runOnWindowsStartup, value);
    }

    public int ApplicationMonthsBefore
    {
        get => _applicationMonthsBefore;
        set => SetProperty(ref _applicationMonthsBefore, Math.Max(0, value));
    }

    public int ApplicationMonthsAfter
    {
        get => _applicationMonthsAfter;
        set => SetProperty(ref _applicationMonthsAfter, Math.Max(0, value));
    }

    public int ReportMonthsBefore
    {
        get => _reportMonthsBefore;
        set => SetProperty(ref _reportMonthsBefore, Math.Max(0, value));
    }

    public int ReportMonthsAfter
    {
        get => _reportMonthsAfter;
        set => SetProperty(ref _reportMonthsAfter, Math.Max(0, value));
    }

    public DateTime ApplicationStart(DateTime referenceDate)
    {
        return new DateTime(referenceDate.Year, referenceDate.Month, 1).AddMonths(-ApplicationMonthsBefore);
    }

    public DateTime ApplicationEnd(DateTime referenceDate)
    {
        return new DateTime(referenceDate.Year, referenceDate.Month, 1).AddMonths(ApplicationMonthsAfter + 1).AddDays(-1);
    }

    public DateTime ReportStart(DateTime referenceDate)
    {
        return new DateTime(referenceDate.Year, referenceDate.Month, 1).AddMonths(-ReportMonthsBefore);
    }

    public DateTime ReportEnd(DateTime referenceDate)
    {
        return new DateTime(referenceDate.Year, referenceDate.Month, 1).AddMonths(ReportMonthsAfter + 1).AddDays(-1);
    }
}
