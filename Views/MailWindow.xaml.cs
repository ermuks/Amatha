using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Amaranth10API.Models;
using Amaranth10API.Services;

namespace Amaranth10API.Views;

public partial class MailWindow : Window
{
    private readonly AmaranthClient _client;
    private readonly AmaranthSession _session;
    private readonly CancellationTokenSource _lifetime = new();
    private InboxMailPage? _page;
    private readonly Dictionary<long, MailBodyWindow> _bodyWindows = new();
    private bool _busy;
    private bool _closed;

    public MailWindow(AmaranthClient client, AmaranthSession session)
    {
        _client = client;
        _session = session;
        InitializeComponent();
        Loaded += FirstLoad;
        Closed += (_, _) => { _closed = true; foreach (var window in _bodyWindows.Values.ToArray()) window.Close(); _lifetime.Cancel(); _lifetime.Dispose(); MailGrid.ItemsSource = null; };
    }

    private void MailGrid_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(MailGrid, source) is DataGridRow row && row.Item is InboxMail mail)
            OpenMail(mail);
    }

    private void MailGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && MailGrid.SelectedItem is InboxMail mail) { e.Handled = true; OpenMail(mail); }
    }

    private void OpenMail(InboxMail mail)
    {
        if (_closed || _busy) return;
        if (_bodyWindows.TryGetValue(mail.Id, out var existing)) { existing.WindowState = WindowState.Normal; existing.Activate(); return; }
        var window = new MailBodyWindow(_client, _session, mail) { Owner = this };
        _bodyWindows.Add(mail.Id, window);
        window.Closed += (_, _) => _bodyWindows.Remove(mail.Id);
        window.Show();
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
