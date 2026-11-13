using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Amaranth10API.Models;
using Amaranth10API.Services;
using Amaranth10API.ViewModels;

namespace Amaranth10API.Views;

/// <summary>
/// 로그인 후 처음 보이는 홈. 미작성 보고서 수(미작성 목록의 뷰모델을 그대로 읽음)와 최근 메일 몇 건만 요약합니다.
/// </summary>
public partial class HomeView : UserControl, IDisposable
{
    /// <summary>홈에 보여 줄 최근 메일 수</summary>
    public const int RecentMailCount = 5;

    private static readonly TimeSpan MailRefreshAge = TimeSpan.FromMinutes(1);

    private readonly AmaranthClient _client;
    private readonly AmaranthSession _session;
    private readonly DashboardViewModel _dashboard;
    private readonly CancellationTokenSource _lifetime = new();
    private DateTime? _mailLoadedAt;
    private bool _loadingMail;
    private bool _closed;

    /// <summary>미작성 보고서 카드를 눌렀을 때</summary>
    public event Action? ReportsRequested;

    /// <summary>메일 카드 머리(null) 또는 메일 한 줄(그 메일)을 눌렀을 때</summary>
    public event Action<InboxMail?>? MailRequested;

    /// <summary>최근 메일을 불러오면 안 읽은 메일 수를 사이드바 배지에 알립니다.</summary>
    public event Action<int>? UnreadCountChanged;

    public HomeView(AmaranthClient client, AmaranthSession session, DashboardViewModel dashboard)
    {
        _client = client;
        _session = session;
        _dashboard = dashboard;
        InitializeComponent();
        TodayText.Text = DateTime.Today.ToString("M월 d일 dddd", System.Globalization.CultureInfo.GetCultureInfo("ko-KR"));
        GreetingText.Text = string.IsNullOrWhiteSpace(session.EmployeeName) ? "안녕하세요" : $"안녕하세요, {session.EmployeeName}님";
        _dashboard.PropertyChanged += Dashboard_PropertyChanged;
        UpdateReport();
        Loaded += (_, _) => RefreshIfStale();
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        _dashboard.PropertyChanged -= Dashboard_PropertyChanged;
        ReportsRequested = null; MailRequested = null; UnreadCountChanged = null;
        _lifetime.Cancel(); _lifetime.Dispose();
    }

    /// <summary>홈에 다시 올 때 부릅니다. 최근 메일을 1분 넘게 안 불렀으면 다시 불러옵니다.</summary>
    public void RefreshIfStale()
    {
        if (_mailLoadedAt == null || DateTime.Now - _mailLoadedAt > MailRefreshAge) _ = LoadMailAsync();
    }

    private void Dashboard_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.TotalCount) or nameof(DashboardViewModel.IsBusy)
            or nameof(DashboardViewModel.OverdueReportCount) or nameof(DashboardViewModel.LastSyncedText) or null or "")
            Dispatcher.BeginInvoke(new Action(UpdateReport));
    }

    private void UpdateReport()
    {
        if (_closed) return;
        bool loaded = !string.IsNullOrEmpty(_dashboard.LastSyncedText);
        if (!loaded)
        {
            ReportCountText.Text = "-";
            ReportCaption.Text = _dashboard.HasError ? "목록을 불러오지 못했어요. 미작성 목록에서 새로고침해 주세요." : "확인하는 중…";
            return;
        }

        int total = _dashboard.TotalCount, overdue = _dashboard.OverdueReportCount;
        ReportCountText.Text = total.ToString();
        ReportCaption.Text = total == 0 ? "모두 작성했어요."
            : overdue > 0 ? $"지금 작성해야 할 보고서 {overdue}건을 포함해요."
            : "아직 기한이 지난 보고서는 없어요.";
    }

    private async Task LoadMailAsync()
    {
        if (_closed || _loadingMail) return;
        _loadingMail = true;
        if (_mailLoadedAt == null) MailStatusText.Text = "메일을 불러오는 중…";
        try
        {
            InboxMailPage page = await _client.GetInboxAsync(_session, 1, RecentMailCount, _lifetime.Token);
            if (_closed) return;
            _mailLoadedAt = DateTime.Now;
            MailList.ItemsSource = page.Items.Take(RecentMailCount).ToList();
            UnreadPill.Visibility = page.UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            UnreadText.Text = $"안 읽음 {page.UnreadCount:N0}";
            MailStatusText.Text = page.Items.Count == 0 ? "받은 메일이 없어요." : string.Empty;
            MailStatusText.Visibility = page.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UnreadCountChanged?.Invoke(page.UnreadCount);
        }
        catch (Exception) when (!_closed)
        {
            MailStatusText.Visibility = Visibility.Visible;
            MailStatusText.Text = "메일을 불러오지 못했어요. 메일 화면에서 새로고침해 주세요.";
        }
        catch (Exception)
        {
            // 닫는 중에 취소된 조회는 무시
        }
        finally
        {
            _loadingMail = false;
        }
    }

    private void Reports_Click(object sender, RoutedEventArgs e) => ReportsRequested?.Invoke();

    private void Mail_Click(object sender, RoutedEventArgs e) => MailRequested?.Invoke(null);

    private void MailRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is InboxMail mail) MailRequested?.Invoke(mail);
    }
}
