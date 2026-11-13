using System.Windows;
using System.Windows.Controls;
using Amaranth10API.Helpers;
using Amaranth10API.Models;
using Amaranth10API.ViewModels;

namespace Amaranth10API.Views;

public partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; } = new(App.Client);

    // "방금 전 동기화" 같은 상대 시간을 30초마다 다시 계산합니다.
    private readonly System.Windows.Threading.DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(30) };

    public DashboardPage(AmaranthSession session)
    {
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += async (_, _) => await ViewModel.InitializeAsync(session);
        _clock.Tick += (_, _) => (DataContext as DashboardViewModel)?.RefreshRelativeTimes();
        Loaded += (_, _) => _clock.Start();
        Unloaded += (_, _) => _clock.Stop();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel dashboard)
        {
            await dashboard.ReloadAsync();
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.OpenSettings();
    }

    private void NotificationSnooze_Click(object sender, RoutedEventArgs e)
    {
        // 체크박스 클릭이 행의 신청서 열기 버튼으로 전달되지 않도록 합니다.
        e.Handled = true;
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is not MissingPeriodViewModel item) return;
        try
        {
            if (DataContext is DashboardViewModel dashboard) dashboard.DismissItem(item);
        }
        catch (Exception exception)
        {
            Toast.Warning(Window.GetWindow(this), "삭제 상태를 저장하지 못했습니다. " + exception.Message, "목록에서 삭제");
        }
    }

    private void Period_Click(object sender, RoutedEventArgs e)
    {
        Window? owner = Window.GetWindow(this);
        AmaranthSession? session = AppRuntime.Current.Session;
        if (session == null)
        {
            Toast.Info(owner, "로그인 세션이 없습니다. 다시 로그인해 주세요.", "신청서 보기");
            return;
        }

        MissingPeriodViewModel? period = (sender as FrameworkElement)?.DataContext as MissingPeriodViewModel;
        if (period is { OpensApplicationDocument: false })
        {
            Toast.Info(
                owner,
                string.IsNullOrWhiteSpace(period.StatusText)
                    ? "대체휴가 요청서를 확인해 주세요."
                    : period.StatusText,
                "대체휴가");
            return;
        }

        try
        {
            if (period?.ApplicationDocument == null)
            {
                Toast.Info(owner, "이 항목에 연결된 신청서를 찾지 못했습니다. ERP에서 신청서를 확인해 주세요.", "신청서 보기");
                return;
            }

            ReportBrowserWindow.OpenApplication(owner, session, period.ApplicationDocument);
        }
        catch (Exception exception)
        {
            Toast.Warning(owner, FormatException(exception), "신청서 보기");
        }
    }

    private static string FormatException(Exception exception)
    {
        List<string> parts = new();
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message) && !parts.Contains(current.Message))
            {
                parts.Add(current.Message);
            }
        }

        return string.Join(Environment.NewLine, parts);
    }
}
