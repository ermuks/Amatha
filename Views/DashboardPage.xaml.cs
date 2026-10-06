using System.Windows;
using System.Windows.Controls;
using Amaranth10API.Helpers;
using Amaranth10API.Models;
using Amaranth10API.ViewModels;

namespace Amaranth10API.Views;

public partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; } = new(App.Client);

    public DashboardPage(AmaranthSession session)
    {
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += async (_, _) => await ViewModel.InitializeAsync(session);
    }

    private void NotificationSnooze_Click(object sender, RoutedEventArgs e)
    {
        // 체크박스 클릭이 카드의 보고서 작성 버튼으로 전달되지 않도록 합니다.
        e.Handled = true;
    }

    private void Period_Click(object sender, RoutedEventArgs e)
    {
        Window? owner = Window.GetWindow(this);
        AmaranthSession? session = AppRuntime.Current.Session;
        if (session == null)
        {
            MessageBox.Show(owner, "로그인 세션이 없습니다. 다시 로그인해 주세요.", "신청서 보기",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MissingPeriodViewModel? period = (sender as FrameworkElement)?.DataContext as MissingPeriodViewModel;
        if (period is { OpensApplicationDocument: false })
        {
            MessageBox.Show(
                owner,
                string.IsNullOrWhiteSpace(period.StatusText)
                    ? "대체휴가 요청서를 확인해 주세요."
                    : period.StatusText,
                "대체휴가",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            if (period?.ApplicationDocument == null)
            {
                MessageBox.Show(owner, "이 항목에 연결된 신청서를 찾지 못했습니다. ERP에서 신청서를 확인해 주세요.", "신청서 보기",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ReportBrowserWindow.OpenApplication(owner, session, period.ApplicationDocument);
        }
        catch (Exception exception)
        {
            MessageBox.Show(owner, FormatException(exception), "신청서 보기", MessageBoxButton.OK, MessageBoxImage.Warning);
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
