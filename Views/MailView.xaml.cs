using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Amaranth10API.Models;
using Amaranth10API.Services;

namespace Amaranth10API.Views;

public partial class MailView : UserControl, IDisposable
{
    private readonly AmaranthClient _client;
    private readonly AmaranthSession _session;
    private readonly CancellationTokenSource _lifetime = new();
    private InboxMailPage? _page;
    private MailBodyView? _bodyView;
    private readonly DispatcherTimer _selectionTimer;
    private InboxMail? _pendingOpen;
    public event Action? DashboardRequested;
    /// <summary>안 읽은 메일 수가 바뀌면 사이드바 배지에 알립니다.</summary>
    public event Action<int>? UnreadCountChanged;
    private bool _busy;
    private bool _closed;

    public MailView(AmaranthClient client, AmaranthSession session)
    {
        _client = client;
        _session = session;
        InitializeComponent();
        _selectionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _selectionTimer.Tick += SelectionTimer_Tick;
        Loaded += FirstLoad;
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        DashboardRequested = null; UnreadCountChanged = null;
        Loaded -= FirstLoad;
        _selectionTimer.Stop(); _selectionTimer.Tick -= SelectionTimer_Tick;
        _bodyView?.Dispose(); _bodyView = null; BodyHost.Content = null;
        _lifetime.Cancel(); _lifetime.Dispose(); MailGrid.ItemsSource = null;
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e) => DashboardRequested?.Invoke();

    /// <summary>오른쪽 본문을 닫고 "메일을 선택해 주세요" 안내로 돌아갑니다. 목록 선택은 그대로 둡니다.</summary>
    private void CloseBody()
    {
        _selectionTimer.Stop();
        _bodyView?.Dispose(); _bodyView = null;
        BodyHost.Content = null; BodyHost.Visibility = Visibility.Collapsed;
        EmptyBodyPanel.Visibility = Visibility.Visible;
        MailGrid.IsEnabled = true;
        MailGrid.Focus();
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

    /// <summary>↑↓ 키로 빠르게 넘길 때 메일마다 서버를 부르지 않도록 선택이 잠시 멈춘 뒤 엽니다. 클릭은 바로 엽니다.</summary>
    private void MailGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectionTimer.Stop();
        if (!_closed && MailGrid.SelectedItem is InboxMail) _selectionTimer.Start();
    }

    private void SelectionTimer_Tick(object? sender, EventArgs e)
    {
        _selectionTimer.Stop();
        if (MailGrid.SelectedItem is InboxMail mail) OpenMail(mail);
    }

    /// <summary>
    /// 다른 화면(홈)에서 고른 메일을 엽니다. 목록에 있으면 그 줄을 선택하고, 목록을 아직 불러오는 중이면 다 불러온 뒤 엽니다.
    /// </summary>
    public void OpenMailFromOutside(InboxMail mail)
    {
        if (_closed) return;
        if (_busy || MailGrid.ItemsSource == null)
        {
            _pendingOpen = mail;
            return;
        }

        _pendingOpen = null;
        InboxMail? match = (MailGrid.ItemsSource as IEnumerable<InboxMail>)?.FirstOrDefault(item => item.Id == mail.Id);
        if (match != null)
        {
            MailGrid.SelectedItem = match;
            MailGrid.ScrollIntoView(match);
        }

        OpenMail(match ?? mail);
    }

    private void OpenMail(InboxMail mail)
    {
        if (_closed || _busy) return;
        if (_bodyView == null)
        {
            _bodyView = new MailBodyView(_client, _session, mail);
            _bodyView.BackRequested += CloseBody;
            // 첨부파일을 받는 동안 다른 메일로 넘어가면 다운로드가 취소되므로 목록을 잠급니다.
            _bodyView.DownloadingChanged += downloading => MailGrid.IsEnabled = !downloading;
            BodyHost.Content = _bodyView;
        }
        else if (_bodyView.MailId != mail.Id)
        {
            if (!_bodyView.ShowMail(mail)) return;
        }

        EmptyBodyPanel.Visibility = Visibility.Collapsed;
        BodyHost.Visibility = Visibility.Visible;
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
            ApplyFilter();
            UnreadCountChanged?.Invoke(result.UnreadCount);
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
            if (!_closed)
            {
                UpdateButtons();
                if (_pendingOpen != null && MailGrid.ItemsSource != null) OpenMailFromOutside(_pendingOpen);
            }
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    /// <summary>현재 페이지 목록을 검색어와 필터(전체·안 읽음·첨부파일)로 거릅니다. 서버 조회는 하지 않습니다.</summary>
    private void ApplyFilter()
    {
        if (MailGrid?.ItemsSource == null) return;
        System.ComponentModel.ICollectionView view = System.Windows.Data.CollectionViewSource.GetDefaultView(MailGrid.ItemsSource);
        if (view == null) return;
        string query = SearchBox?.Text?.Trim() ?? string.Empty;
        bool unreadOnly = FilterUnread?.IsChecked == true;
        bool attachmentOnly = FilterAttachment?.IsChecked == true;
        if (query.Length == 0 && !unreadOnly && !attachmentOnly)
        {
            view.Filter = null;
            return;
        }

        view.Filter = item => item is InboxMail mail
            && (!unreadOnly || mail.IsUnread)
            && (!attachmentOnly || mail.HasAttachment)
            && (query.Length == 0
                || mail.Subject.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0
                || mail.SenderName.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0
                || mail.SenderAddress.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0);
    }

    private void UpdateButtons()
    {
        RefreshButton.IsEnabled = !_busy;
        PreviousButton.IsEnabled = !_busy && _page?.Page > 1;
        NextButton.IsEnabled = !_busy && _page?.HasNext == true;
    }
}
