using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Amaranth10API.Helpers;
using Amaranth10API.Models;
using Amaranth10API.Services;

namespace Amaranth10API.ViewModels;

public sealed class LoginViewModel : ObservableObject
{
    private string _loginId = string.Empty;
    private string _password = string.Empty;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private bool _isWaitingForNetwork;

    public string LoginId
    {
        get => _loginId;
        set => SetProperty(ref _loginId, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(CanEditLogin));
                OnPropertyChanged(nameof(LoginButtonText));
            }
        }
    }

    public bool IsWaitingForNetwork
    {
        get => _isWaitingForNetwork;
        private set
        {
            if (SetProperty(ref _isWaitingForNetwork, value))
            {
                OnPropertyChanged(nameof(CanEditLogin));
            }
        }
    }

    public bool IsIdle => !IsBusy;

    public bool CanEditLogin => !IsBusy && !IsWaitingForNetwork;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public string LoginButtonText => IsBusy ? "로그인 중" : "로그인";

    public void BeginNetworkWait()
    {
        ErrorMessage = string.Empty;
        StatusMessage = "인터넷 연결 대기 중...";
        IsWaitingForNetwork = true;
    }

    public void EndNetworkWait()
    {
        IsWaitingForNetwork = false;
        if (StatusMessage == "인터넷 연결 대기 중...")
        {
            StatusMessage = string.Empty;
        }
    }
}

public sealed class DashboardViewModel : ObservableObject
{
    private readonly AmaranthClient _client;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private AmaranthSession? _session;
    private string _statusMessage = "데이터를 불러오는 중...";
    private string _errorMessage = string.Empty;
    private string _tripEmptyText = string.Empty;
    private string _holidayEmptyText = string.Empty;
    private string _substituteEmptyText = string.Empty;
    private bool _isBusy = true;
    private bool _isAllClear;
    private bool _hasMissingTrips;
    private bool _hasMissingHolidayWork;
    private bool _hasSubstituteIssues;
    private int _missingTripCount;
    private int _missingHolidayCount;
    private int _substituteIssueCount;

    public DashboardViewModel(AmaranthClient client)
    {
        _client = client;
    }

    public ObservableCollection<MissingPeriodViewModel> MissingTrips { get; } = new();
    public ObservableCollection<MissingPeriodViewModel> MissingHolidayWorks { get; } = new();
    public ObservableCollection<MissingPeriodViewModel> SubstituteHolidayIssues { get; } = new();

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public string TripEmptyText
    {
        get => _tripEmptyText;
        private set => SetProperty(ref _tripEmptyText, value);
    }

    public string HolidayEmptyText
    {
        get => _holidayEmptyText;
        private set => SetProperty(ref _holidayEmptyText, value);
    }

    public string SubstituteEmptyText
    {
        get => _substituteEmptyText;
        private set => SetProperty(ref _substituteEmptyText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    public bool IsIdle => !IsBusy;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsAllClear
    {
        get => _isAllClear;
        private set => SetProperty(ref _isAllClear, value);
    }

    public bool HasMissingTrips
    {
        get => _hasMissingTrips;
        private set => SetProperty(ref _hasMissingTrips, value);
    }

    public bool HasMissingHolidayWork
    {
        get => _hasMissingHolidayWork;
        private set => SetProperty(ref _hasMissingHolidayWork, value);
    }

    public bool HasSubstituteIssues
    {
        get => _hasSubstituteIssues;
        private set => SetProperty(ref _hasSubstituteIssues, value);
    }

    public int MissingTripCount
    {
        get => _missingTripCount;
        private set
        {
            if (SetProperty(ref _missingTripCount, value))
            {
                OnPropertyChanged(nameof(TripCountLabel));
            }
        }
    }

    public int MissingHolidayCount
    {
        get => _missingHolidayCount;
        private set
        {
            if (SetProperty(ref _missingHolidayCount, value))
            {
                OnPropertyChanged(nameof(HolidayCountLabel));
            }
        }
    }

    public int SubstituteIssueCount
    {
        get => _substituteIssueCount;
        private set
        {
            if (SetProperty(ref _substituteIssueCount, value))
            {
                OnPropertyChanged(nameof(SubstituteCountLabel));
            }
        }
    }

    public string TripCountLabel => FormatCount(MissingTripCount);

    public string HolidayCountLabel => FormatCount(MissingHolidayCount);

    public string SubstituteCountLabel => FormatCount(SubstituteIssueCount);

    public async Task InitializeAsync(AmaranthSession session)
    {
        _session = session;
        await LoadAsync();
    }

    public async Task ReloadAsync(bool silent = false)
    {
        await LoadAsync(silent);
    }

    private async Task LoadAsync(bool silent = false)
    {
        if (_session == null)
        {
            return;
        }

        if (!await _loadLock.WaitAsync(silent ? TimeSpan.Zero : Timeout.InfiniteTimeSpan))
        {
            return;
        }

        try
        {
            if (_session == null)
            {
                return;
            }

            if (!silent)
            {
                IsBusy = true;
            }

            ErrorMessage = string.Empty;
            if (!silent)
            {
                StatusMessage = "데이터를 불러오는 중...";
            }

            Progress<string>? progress = silent ? null : new Progress<string>(message => StatusMessage = message);
            await _client.LoadDashboardAsync(_session, DateTime.Today, AppRuntime.Current.Settings, progress);
            ApplyMissingPeriods();
            NotifyIfNeeded();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            StatusMessage = "불러오지 못했습니다.";
        }
        finally
        {
            if (!silent)
            {
                IsBusy = false;
            }

            _loadLock.Release();
        }
    }

    private void NotifyIfNeeded()
    {
        AppSettings settings = AppRuntime.Current.Settings;
        if (!settings.NotificationsEnabled)
        {
            return;
        }

        WindowsNotification.ShowMissingReports(
            MissingTrips.Count(item => item.ShouldNotify),
            MissingHolidayWorks.Count(item => item.ShouldNotify),
            SubstituteHolidayIssues.Count(item => item.ShouldNotify && !item.IsTimeMismatch),
            SubstituteHolidayIssues.Count(item => item.ShouldNotify && item.IsTimeMismatch),
            settings.ShowGuiOnNotification);
    }

    private void ApplyMissingPeriods()
    {
        MissingTrips.Clear();
        MissingHolidayWorks.Clear();
        SubstituteHolidayIssues.Clear();

        foreach (MissingReportPeriod period in _client.GetMissingTripReportPeriods())
        {
            MissingTrips.Add(CreateItem(period));
        }

        foreach (MissingReportPeriod period in _client.GetMissingHolidayWorkReportPeriods())
        {
            MissingHolidayWorks.Add(CreateItem(period));
        }

        foreach (SubstituteHolidayIssue issue in _client.GetSubstituteHolidayIssues())
        {
            SubstituteHolidayIssues.Add(CreateSubstituteItem(issue));
        }

        MissingTripCount = MissingTrips.Count;
        MissingHolidayCount = MissingHolidayWorks.Count;
        SubstituteIssueCount = SubstituteHolidayIssues.Count;
        HasMissingTrips = MissingTripCount > 0;
        HasMissingHolidayWork = MissingHolidayCount > 0;
        HasSubstituteIssues = SubstituteIssueCount > 0;
        IsAllClear = !HasMissingTrips && !HasMissingHolidayWork && !HasSubstituteIssues;
        TripEmptyText = HasMissingTrips ? string.Empty : "출장 보고서는 모두 작성되어 있습니다.";
        HolidayEmptyText = HasMissingHolidayWork ? string.Empty : "휴일근무 보고서는 모두 작성되어 있습니다.";
        SubstituteEmptyText = HasSubstituteIssues ? string.Empty : "대체휴가 요청서는 모두 맞습니다.";
    }

    private static MissingPeriodViewModel CreateItem(MissingReportPeriod period)
    {
        BusinessTripDocument? application = period.RelatedApplication;
        bool hasApplication = application != null;
        DateTime today = DateTime.Today;
        DateTime startDate = period.StartDate.Date;
        DateTime endDate = period.EndDate.Date;
        bool isUpcoming = today < startDate;
        bool isOngoing = today >= startDate && today <= endDate;
        bool isTrip = period.Kind == "출장" || period.Name.Contains("출장");

        return new MissingPeriodViewModel
        {
            Name = period.Name,
            PeriodText = FormatPeriod(period.StartDate, period.EndDate),
            DayCountText = period.DayCount == 1 ? "1일" : $"{period.DayCount}일",
            Hint = hasApplication ? application!.Title : string.Empty,
            ProjectCode = application?.ProjectCode ?? string.Empty,
            StatusText = BuildStatusText(application, isTrip, isUpcoming, isOngoing),
            StartDate = period.StartDate,
            EndDate = period.EndDate,
            IsOngoing = isOngoing,
            ShouldNotify = today > endDate,
            Fill = new ReportDraftFill
            {
                StartDate = period.StartDate.Date,
                EndDate = period.EndDate.Date,
                StartTime = application?.StartTime ?? string.Empty,
                EndTime = application?.EndTime ?? string.Empty,
                Kind = period.Kind,
                HolidayDays = CreateHolidayDays(period, application)
            },
            AccentBrush = isOngoing
                ? new SolidColorBrush(Color.FromRgb(91, 138, 122))
                : SchedulePalette.GetBrush(period.Name)
        };
    }

    private static MissingPeriodViewModel CreateSubstituteItem(SubstituteHolidayIssue issue)
    {
        DateTime workDate = issue.WorkDate.Date;
        string reportTime = FormatTimeRange(issue.ReportStartTime, issue.ReportEndTime);
        string requestTime = FormatTimeRange(issue.RequestStartTime, issue.RequestEndTime);
        string status = issue.IsTimeMismatch
            ? $"보고서는 {reportTime}인데 요청서는 {requestTime}입니다"
            : "대체휴가 요청서가 없습니다";
        string hint = issue.IsTimeMismatch
            ? issue.RequestTitle
            : issue.ReportTitle;

        return new MissingPeriodViewModel
        {
            Name = issue.IsTimeMismatch ? "대체휴가 시간 오류" : "대체휴가 미작성",
            PeriodText = FormatPeriod(workDate, workDate),
            DayCountText = issue.IsTimeMismatch ? "오류" : "미작성",
            Hint = hint,
            StatusText = status,
            StartDate = workDate,
            EndDate = workDate,
            ShouldNotify = DateTime.Today > workDate,
            IsTimeMismatch = issue.IsTimeMismatch,
            OpensReportDraft = false,
            AccentBrush = SchedulePalette.GetBrush("대체휴가")
        };
    }

    private static string FormatCount(int count) => count == 0 ? "없음" : $"{count}건";

    private static string FormatTimeRange(string startTime, string endTime)
    {
        if (string.IsNullOrWhiteSpace(startTime) || string.IsNullOrWhiteSpace(endTime))
        {
            return "-";
        }

        return $"{startTime}~{endTime}";
    }

    private static string FormatPeriod(DateTime startDate, DateTime endDate)
    {
        if (startDate.Date == endDate.Date)
        {
            return $"{startDate.Year}년 {startDate.Month}월 {startDate.Day}일";
        }

        if (startDate.Year == endDate.Year && startDate.Month == endDate.Month)
        {
            return $"{startDate.Year}년 {startDate.Month}월 {startDate.Day}일 – {endDate.Day}일";
        }

        if (startDate.Year == endDate.Year)
        {
            return $"{startDate.Year}년 {startDate.Month}월 {startDate.Day}일 – {endDate.Month}월 {endDate.Day}일";
        }

        return $"{startDate.Year}년 {startDate.Month}월 {startDate.Day}일 – {endDate.Year}년 {endDate.Month}월 {endDate.Day}일";
    }

    private static List<ReportHolidayDayFill> CreateHolidayDays(
        MissingReportPeriod period,
        BusinessTripDocument? application)
    {
        List<ReportHolidayDayFill> days = new();
        if (period.Kind != "휴일근무")
        {
            return days;
        }

        for (DateTime date = period.StartDate.Date; date <= period.EndDate.Date; date = date.AddDays(1))
        {
            string startTime = string.Empty;
            string endTime = string.Empty;
            if (application?.WorkDays.Count > 0)
            {
                HolidayWorkDay? workDay = application.WorkDays.Find(day => day.WorkDate.Date == date);
                startTime = workDay?.StartTime ?? string.Empty;
                endTime = workDay?.EndTime ?? string.Empty;
            }
            else
            {
                startTime = application?.StartTime ?? string.Empty;
                endTime = application?.EndTime ?? string.Empty;
            }

            days.Add(new ReportHolidayDayFill
            {
                Year = date.Year,
                Month = date.Month,
                Day = date.Day,
                Weekday = KoreanWeekday(date),
                StartTime = startTime,
                EndTime = endTime
            });
        }

        return days;
    }

    private static string BuildStatusText(
        BusinessTripDocument? application,
        bool isTrip,
        bool isUpcoming,
        bool isOngoing)
    {
        string status = NotificationStatusText(isTrip, isUpcoming, isOngoing);
        if (application != null &&
            ApprovalStatus.IsInProgress(application.DocumentStatus, application.DocumentStatusCode))
        {
            const string pending = "결재가 아직 종결되지 않았습니다";
            status = string.IsNullOrWhiteSpace(status) ? pending : status + " · " + pending;
        }

        return status;
    }

    private static string NotificationStatusText(bool isTrip, bool isUpcoming, bool isOngoing)
    {
        if (isUpcoming)
        {
            return isTrip
                ? "출장 전 · 알림을 보내지 않습니다"
                : "휴일근무 전 · 알림을 보내지 않습니다";
        }

        if (isOngoing)
        {
            return isTrip
                ? "현재 출장 중 · 알림을 보내지 않습니다"
                : "휴일근무 중 · 알림을 보내지 않습니다";
        }

        return string.Empty;
    }

    private static string KoreanWeekday(DateTime date)
    {
        return date.DayOfWeek switch
        {
            DayOfWeek.Sunday => "일",
            DayOfWeek.Monday => "월",
            DayOfWeek.Tuesday => "화",
            DayOfWeek.Wednesday => "수",
            DayOfWeek.Thursday => "목",
            DayOfWeek.Friday => "금",
            DayOfWeek.Saturday => "토",
            _ => string.Empty
        };
    }
}

public sealed class MissingPeriodViewModel
{
    public string Name { get; init; } = string.Empty;
    public string PeriodText { get; init; } = string.Empty;
    public string DayCountText { get; init; } = string.Empty;
    public string Hint { get; init; } = string.Empty;
    public string ProjectCode { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public bool IsOngoing { get; init; }
    public bool ShouldNotify { get; init; }
    public bool IsTimeMismatch { get; init; }
    public bool OpensReportDraft { get; init; } = true;
    public ReportDraftFill Fill { get; init; } = new();
    public SolidColorBrush AccentBrush { get; init; } = SchedulePalette.GetBrush("출장");
    public Visibility HintVisibility => string.IsNullOrWhiteSpace(Hint) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ProjectCodeVisibility => string.IsNullOrWhiteSpace(ProjectCode) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility StatusVisibility => string.IsNullOrWhiteSpace(StatusText) ? Visibility.Collapsed : Visibility.Visible;
}
