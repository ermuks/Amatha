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

    private void Period_Click(object sender, RoutedEventArgs e)
    {
        Window? owner = Window.GetWindow(this);
        AmaranthSession? session = AppRuntime.Current.Session;
        if (session == null)
        {
            MessageBox.Show(owner, "로그인 세션이 없습니다. 다시 로그인해 주세요.", "보고서 작성",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MissingPeriodViewModel? period = (sender as FrameworkElement)?.DataContext as MissingPeriodViewModel;

        try
        {
            ReportBrowserWindow.OpenDraft(owner, session, period?.Fill);
        }
        catch (Exception exception)
        {
            MessageBox.Show(owner, FormatException(exception), "보고서 작성", MessageBoxButton.OK, MessageBoxImage.Warning);
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
