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
    private readonly NotificationSnoozeStore _notificationSnoozes;
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
    private string _educationEmptyText = string.Empty;
    private bool _hasMissingEducation;
    private int _missingEducationCount;

    public DashboardViewModel(AmaranthClient client, NotificationSnoozeStore? notificationSnoozes = null)
    {
        _client = client;
        _notificationSnoozes = notificationSnoozes ?? AppRuntime.Current.NotificationSnoozes;
    }

    public ObservableCollection<MissingPeriodViewModel> MissingTrips { get; } = new();
    public ObservableCollection<MissingPeriodViewModel> MissingHolidayWorks { get; } = new();
    public ObservableCollection<MissingPeriodViewModel> SubstituteHolidayIssues { get; } = new();

    /// <summary>교육결과보고서를 쓰지 않은 교육 (사외교육신청서·영어교육계획서 …)</summary>
    public ObservableCollection<MissingPeriodViewModel> MissingEducations { get; } = new();

    public string EducationEmptyText
    {
        get => _educationEmptyText;
        private set => SetProperty(ref _educationEmptyText, value);
    }

    public bool HasMissingEducation
    {
        get => _hasMissingEducation;
        private set => SetProperty(ref _hasMissingEducation, value);
    }

    public int MissingEducationCount
    {
        get => _missingEducationCount;
        private set
        {
            if (SetProperty(ref _missingEducationCount, value))
            {
                OnPropertyChanged(nameof(EducationCountLabel));
            }
        }
    }

    public string EducationCountLabel => FormatCount(MissingEducationCount);

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

    // ───── v1.14.0 리디자인: 화면 머리말·요약·배너 ─────
    private DateTime? _lastLoadedAt;

    /// <summary>예: "11월 13일 금요일"</summary>
    public string TodayText => DateTime.Today.ToString("M월 d일 dddd", System.Globalization.CultureInfo.GetCultureInfo("ko-KR"));

    public string GreetingText => string.IsNullOrWhiteSpace(_session?.EmployeeName)
        ? "안녕하세요"
        : $"안녕하세요, {_session!.EmployeeName}님";

    public string SubtitleText => string.IsNullOrWhiteSpace(_session?.DepartmentName)
        ? "작성하지 않은 출장·휴일근무·교육 보고서를 모아 보여드려요."
        : $"{_session!.DepartmentName} · 작성하지 않은 출장·휴일근무·교육 보고서를 모아 보여드려요.";

    public string LastSyncedText
    {
        get
        {
            if (_lastLoadedAt == null) return string.Empty;
            TimeSpan elapsed = DateTime.Now - _lastLoadedAt.Value;
            if (elapsed.TotalMinutes < 1) return "방금 전 동기화";
            if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}분 전 동기화";
            return $"{_lastLoadedAt.Value:HH:mm} 동기화";
        }
    }

    /// <summary>기한이 지나 지금 작성해야 하는 출장·휴일근무·교육 보고서 수</summary>
    public int OverdueReportCount => MissingTrips.Count(item => item.ShouldNotify) + MissingHolidayWorks.Count(item => item.ShouldNotify)
        + MissingEducations.Count(item => item.ShouldNotify);

    /// <summary>목록에 보이는 전체 항목 수 (사이드바 배지)</summary>
    public int TotalCount => MissingTripCount + MissingHolidayCount + SubstituteIssueCount + MissingEducationCount;

    /// <summary>배너 종류: attention(보라) · calm(보라, 기한 전) · clear(초록)</summary>
    public string BannerKind => OverdueReportCount > 0 || SubstituteIssueCount > 0
        ? "attention"
        : TotalCount > 0 ? "calm" : "clear";

    public string BannerTitle
    {
        get
        {
            if (OverdueReportCount > 0) return $"작성할 보고서가 {OverdueReportCount}건 있어요.";
            if (SubstituteIssueCount > 0) return "대체휴가 요청서를 확인해 주세요.";
            if (TotalCount > 0) return "기한이 지난 보고서는 없어요.";
            return "모든 보고서를 작성했어요.";
        }
    }

    public string BannerMessage
    {
        get
        {
            if (OverdueReportCount > 0 || SubstituteIssueCount > 0) return "항목을 누르면 연결된 신청서를 볼 수 있어요.";
            if (TotalCount > 0) return "일정이 끝나면 작성 알림을 보내드릴게요.";
            return "새 일정이 끝나면 다시 알려드릴게요.";
        }
    }

    /// <summary>화면의 "N분 전 동기화" 같은 상대 시간을 다시 계산합니다.</summary>
    public void RefreshRelativeTimes()
    {
        OnPropertyChanged(nameof(LastSyncedText));
        OnPropertyChanged(nameof(TodayText));
    }

    public async Task InitializeAsync(AmaranthSession session)
    {
        _session = session;
        OnPropertyChanged(nameof(GreetingText));
        OnPropertyChanged(nameof(SubtitleText));
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
            _lastLoadedAt = DateTime.Now;
            RefreshRelativeTimes();
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
        bool suppressActivation = App.ConsumeInitialNotificationSuppression();
        AppSettings settings = AppRuntime.Current.Settings;
        if (!settings.NotificationsEnabled)
        {
            return;
        }

        WindowsNotification.ShowMissingReports(
            MissingTrips.Count(item => item.CanNotify),
            MissingHolidayWorks.Count(item => item.CanNotify),
            SubstituteHolidayIssues.Count(item => item.CanNotify && !item.IsTimeMismatch),
            SubstituteHolidayIssues.Count(item => item.CanNotify && item.IsTimeMismatch),
            settings.ShowGuiOnNotification && !suppressActivation,
            MissingEducations.Count(item => item.CanNotify));
    }

    private void ApplyMissingPeriods()
    {
        MissingTrips.Clear();
        MissingHolidayWorks.Clear();
        SubstituteHolidayIssues.Clear();
        MissingEducations.Clear();

        foreach (MissingEducationReport education in _client.GetMissingEducationReports())
        {
            var item = CreateEducationItem(education);
            if (!item.IsDismissed) MissingEducations.Add(item);
        }

        foreach (MissingReportPeriod period in _client.GetMissingTripReportPeriods())
        {
            var item = CreateItem(period);
            if (!item.IsDismissed) MissingTrips.Add(item);
        }

        foreach (MissingReportPeriod period in _client.GetMissingHolidayWorkReportPeriods())
        {
            var item = CreateItem(period);
            if (!item.IsDismissed) MissingHolidayWorks.Add(item);
        }

        foreach (SubstituteHolidayIssue issue in _client.GetSubstituteHolidayIssues())
        {
            var item = CreateSubstituteItem(issue);
            if (!item.IsDismissed) SubstituteHolidayIssues.Add(item);
        }

        UpdateCounts();
    }

    public void DismissItem(MissingPeriodViewModel item)
    {
        if (!MissingTrips.Contains(item) && !MissingHolidayWorks.Contains(item) && !SubstituteHolidayIssues.Contains(item)
            && !MissingEducations.Contains(item)) return;
        item.Dismiss();
        MissingTrips.Remove(item);
        MissingHolidayWorks.Remove(item);
        SubstituteHolidayIssues.Remove(item);
        MissingEducations.Remove(item);
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        MissingTripCount = MissingTrips.Count;
        MissingHolidayCount = MissingHolidayWorks.Count;
        SubstituteIssueCount = SubstituteHolidayIssues.Count;
        MissingEducationCount = MissingEducations.Count;
        HasMissingTrips = MissingTripCount > 0;
        HasMissingHolidayWork = MissingHolidayCount > 0;
        HasSubstituteIssues = SubstituteIssueCount > 0;
        HasMissingEducation = MissingEducationCount > 0;
        IsAllClear = !HasMissingTrips && !HasMissingHolidayWork && !HasSubstituteIssues && !HasMissingEducation;
        EducationEmptyText = HasMissingEducation ? string.Empty : "교육결과보고서는 모두 작성되어 있습니다.";
        TripEmptyText = HasMissingTrips ? string.Empty : "출장 보고서는 모두 작성되어 있습니다.";
        HolidayEmptyText = HasMissingHolidayWork ? string.Empty : "휴일근무 보고서는 모두 작성되어 있습니다.";
        SubstituteEmptyText = HasSubstituteIssues ? string.Empty : "대체휴가 요청서는 모두 맞습니다.";
        OnPropertyChanged(nameof(OverdueReportCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(BannerKind));
        OnPropertyChanged(nameof(BannerTitle));
        OnPropertyChanged(nameof(BannerMessage));
    }

    private MissingPeriodViewModel CreateItem(MissingReportPeriod period)
    {
        BusinessTripDocument? application = period.RelatedApplication;
        bool hasApplication = application != null;
        DateTime today = DateTime.Today;
        DateTime startDate = period.StartDate.Date;
        DateTime endDate = period.EndDate.Date;
        bool isUpcoming = today < startDate;
        bool isOngoing = today >= startDate && today <= endDate;
        bool isTrip = period.Kind == "출장" || period.Name.Contains("출장");

        return new MissingPeriodViewModel(
            _notificationSnoozes,
            CreateNotificationKey(period.Kind + ":" + period.Name, startDate, endDate))
        {
            Name = period.Name,
            PeriodText = FormatPeriod(period.StartDate, period.EndDate),
            DayCountText = period.DayCount == 1 ? "1일" : $"{period.DayCount}일",
            Hint = hasApplication ? application!.Title : string.Empty,
            ProjectCode = application?.ProjectCode ?? string.Empty,
            ApplicationDocument = application,
            StatusText = BuildStatusText(application, isTrip, isUpcoming, isOngoing),
            StartDate = period.StartDate,
            EndDate = period.EndDate,
            IsOngoing = isOngoing,
            ShouldNotify = today > endDate,
            TileKind = isTrip ? "trip" : "holiday",
            Stage = isUpcoming ? "upcoming" : isOngoing ? "ongoing" : "overdue",
            ChipText = isUpcoming ? "예정" : isOngoing ? "진행 중" : "작성 필요",
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

    /// <summary>교육결과보고서 미작성 항목. 교육이 끝난 다음 날부터 알림 대상</summary>
    private MissingPeriodViewModel CreateEducationItem(MissingEducationReport education)
    {
        DateTime today = DateTime.Today;
        bool isUpcoming = today < education.StartDate;
        bool isOngoing = !isUpcoming && today <= education.EndDate;
        BusinessTripDocument application = education.Application;

        List<string> status = new();
        if (isUpcoming) status.Add("교육 전 · 알림을 보내지 않습니다");
        else if (isOngoing) status.Add("교육 중 · 알림을 보내지 않습니다");
        if (education.PeriodGuessed) status.Add("신청서에서 교육 기간을 찾지 못해 결재일로 판단했습니다");
        if (ApprovalStatus.IsInProgress(application.DocumentStatus, application.DocumentStatusCode))
            status.Add("결재가 아직 종결되지 않았습니다");

        return new MissingPeriodViewModel(
            _notificationSnoozes,
            CreateNotificationKey("교육:" + application.DocumentId, education.StartDate, education.EndDate))
        {
            Name = education.KindName,
            PeriodText = FormatPeriod(education.StartDate, education.EndDate),
            DayCountText = education.DayCount == 1 ? "1일" : $"{education.DayCount}일",
            Hint = string.IsNullOrWhiteSpace(education.CourseName) ? application.Title : education.CourseName,
            ApplicationDocument = application,
            StatusText = string.Join(" · ", status),
            StartDate = education.StartDate,
            EndDate = education.EndDate,
            IsOngoing = isOngoing,
            ShouldNotify = today > education.EndDate,
            TileKind = "education",
            Stage = isUpcoming ? "upcoming" : isOngoing ? "ongoing" : "overdue",
            ChipText = isUpcoming ? "예정" : isOngoing ? "진행 중" : "작성 필요",
            AccentBrush = SchedulePalette.GetBrush("교육")
        };
    }

    private MissingPeriodViewModel CreateSubstituteItem(SubstituteHolidayIssue issue)
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

        return new MissingPeriodViewModel(
            _notificationSnoozes,
            CreateNotificationKey(issue.IsTimeMismatch ? "대체휴가:시간오류" : "대체휴가:미작성", workDate, workDate))
        {
            Name = issue.IsTimeMismatch ? "대체휴가 시간 오류" : "대체휴가 미작성",
            PeriodText = FormatPeriod(workDate, workDate),
            DayCountText = string.Empty,
            TileKind = issue.IsTimeMismatch ? "error" : "substitute",
            Stage = "problem",
            ChipText = issue.IsTimeMismatch ? "시간 오류" : "요청서 없음",
            Hint = hint,
            StatusText = status,
            StartDate = workDate,
            EndDate = workDate,
            ShouldNotify = DateTime.Today > workDate,
            IsTimeMismatch = issue.IsTimeMismatch,
            OpensApplicationDocument = false,
            AccentBrush = SchedulePalette.GetBrush("대체휴가")
        };
    }

    private string CreateNotificationKey(string kind, DateTime start, DateTime end)
    {
        // 사용자와 날짜·종류로 식별해 카드가 새로 만들어져도 체크 상태를 유지합니다.
        return $"{_session?.CompanySequence}|{_session?.EmployeeSequence}|{kind}|{start:yyyyMMdd}|{end:yyyyMMdd}";
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

public sealed class MissingPeriodViewModel : ObservableObject
{
    private readonly NotificationSnoozeStore _notificationSnoozes;
    private readonly string _notificationKey;

    public MissingPeriodViewModel(NotificationSnoozeStore notificationSnoozes, string notificationKey)
    {
        _notificationSnoozes = notificationSnoozes;
        _notificationKey = notificationKey;
    }

    public bool IsNotificationSnoozed
    {
        get => _notificationSnoozes.IsSnoozed(_notificationKey);
        set
        {
            if (value == IsNotificationSnoozed)
            {
                return;
            }

            _notificationSnoozes.SetSnoozed(_notificationKey, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanNotify));
        }
    }

    public bool IsDismissed => _notificationSnoozes.IsDismissed(_notificationKey);

    public void Dismiss()
    {
        _notificationSnoozes.Dismiss(_notificationKey);
        OnPropertyChanged(nameof(IsDismissed));
        OnPropertyChanged(nameof(CanNotify));
    }

    public bool CanNotify => ShouldNotify && !IsNotificationSnoozed && !IsDismissed;

    public string Name { get; init; } = string.Empty;
    public string PeriodText { get; init; } = string.Empty;
    public string DayCountText { get; init; } = string.Empty;
    public string Hint { get; init; } = string.Empty;
    public string ProjectCode { get; init; } = string.Empty;
    public BusinessTripDocument? ApplicationDocument { get; init; }
    public string StatusText { get; init; } = string.Empty;
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public bool IsOngoing { get; init; }
    public bool ShouldNotify { get; init; }
    public bool IsTimeMismatch { get; init; }
    public bool OpensApplicationDocument { get; init; } = true;
    public ReportDraftFill Fill { get; init; } = new();
    public SolidColorBrush AccentBrush { get; init; } = SchedulePalette.GetBrush("출장");
    public Visibility HintVisibility => string.IsNullOrWhiteSpace(Hint) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ProjectCodeVisibility => string.IsNullOrWhiteSpace(ProjectCode) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility StatusVisibility => string.IsNullOrWhiteSpace(StatusText) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DayCountVisibility => string.IsNullOrWhiteSpace(DayCountText) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>유형 타일: trip · holiday · substitute · error</summary>
    public string TileKind { get; init; } = "trip";

    /// <summary>상태 칩 색: overdue(빨강) · ongoing(주황) · upcoming(파랑) · problem(빨강)</summary>
    public string Stage { get; init; } = "overdue";

    public string ChipText { get; init; } = "작성 필요";
}
