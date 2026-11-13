using System.Threading;
using System.Windows;
using Amaranth10API.Models;
using Amaranth10API.Services;

namespace Amaranth10API.Views;

public partial class MailWindow : Window
{
    private readonly AmaranthClient _client;
    private readonly AmaranthSession _session;
    private readonly CancellationTokenSource _lifetime = new();
    private InboxMailPage? _page;
    private bool _busy;
    private bool _closed;

    public MailWindow(AmaranthClient client, AmaranthSession session)
    {
        _client = client;
        _session = session;
        InitializeComponent();
        Loaded += FirstLoad;
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); _lifetime.Dispose(); MailGrid.ItemsSource = null; };
    }

    private async void FirstLoad(object sender, RoutedEventArgs e)
    {
        Loaded -= FirstLoad;
        await LoadPageAsync(1);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadPageAsync(1);
    private async void Previous_Click(object sender, RoutedEventArgs e) => await LoadPageAsync(Math.Max(1, (_page?.Page ?? 1) - 1));
    private async void Next_Click(object sender, RoutedEventArgs e) => await LoadPageAsync((_page?.Page ?? 1) + 1);

    private async Task LoadPageAsync(int page)
    {
        if (_busy || _closed) return;
        _busy = true;
        UpdateButtons();
        StatusPanel.Visibility = Visibility.Visible;
        StatusText.Text = "받은 메일을 불러오는 중입니다…";
        try
        {
            InboxMailPage result = await _client.GetInboxAsync(_session, page, 100, _lifetime.Token);
            if (_closed) return;
            _page = result;
            MailGrid.ItemsSource = result.Items;
            if (result.Items.Count > 0) MailGrid.ScrollIntoView(result.Items[0]);
            SummaryText.Text = $"전체 {result.TotalCount:N0}건 · 안 읽은 메일 {result.UnreadCount:N0}건";
            PageText.Text = $"{result.Page} / {Math.Max(1, (int)Math.Ceiling(result.TotalCount / 100.0))} 페이지";
            StatusText.Text = result.TotalCount == 0 ? "받은 메일이 없습니다." : "이 페이지에 메일이 없습니다. 새로고침해 주세요.";
            StatusPanel.Visibility = result.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            if (!_closed) StatusText.Text = "조회 시간이 초과되었습니다. 새로고침으로 다시 시도해 주세요.";
        }
        catch (Exception)
        {
            if (!_closed) StatusText.Text = "메일을 조회하지 못했습니다. 새로고침하거나 다시 로그인해 주세요.";
        }
        finally
        {
            _busy = false;
            if (!_closed) UpdateButtons();
        }
    }

    private void UpdateButtons()
    {
        RefreshButton.IsEnabled = !_busy;
        PreviousButton.IsEnabled = !_busy && _page?.Page > 1;
        NextButton.IsEnabled = !_busy && _page?.HasNext == true;
    }
}
