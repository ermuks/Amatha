using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Amaranth10API.Helpers;
using Amaranth10API.Models;
using Amaranth10API.Services;
using Amaranth10API.ViewModels;
using Amaranth10API.Views;
using Forms = System.Windows.Forms;

namespace Amaranth10API;

public partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon _tray = new();
    private TrayMenuWindow? _trayMenu;
    private MailView? _mailView;
    private ManualView? _manualView;

    /// <summary>콘텐츠 패널에 지금 보이는 화면 (환경설정은 그 위에 덮는 화면이라 따로 다룸)</summary>
    private enum ShellContent { Dashboard, Mail, Manual }
    private ShellContent _content = ShellContent.Dashboard;
    private bool _forceClose;
    private bool _hideToTrayAfterRender;
    private AppUpdateInfo? _availableUpdate;
    private bool _updateBusy;
    private bool _updateCheckRunning;
    private string _updateLinkText = string.Empty;
    private readonly DispatcherTimer _updateTimer = new()
    {
        Interval = TimeSpan.FromHours(1)
    };

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = AppVersion.FooterLabel;
        SettingsOverlay.DataContext = AppRuntime.Current.Settings;
        InitializeTray();
        NavigateToLogin();
        Loaded += MainWindow_Loaded;
        _updateTimer.Tick += async (_, _) => await CheckForUpdateAsync();
        _updateTimer.Start();
        _ = CheckForUpdateAsync();
        if (App.StartedInTray || (AppRuntime.Current.Settings.StartInTray && App.StartedFromWindowsStartup))
        {
            ShowInTaskbar = false;
            ShowActivated = false;
            Opacity = 0;
            _hideToTrayAfterRender = true;
            ContentRendered += HideToTrayAfterFirstRender;
        }
    }

    private void HideToTrayAfterFirstRender(object? sender, EventArgs e)
    {
        ContentRendered -= HideToTrayAfterFirstRender;
        if (!_hideToTrayAfterRender)
        {
            return;
        }

        _hideToTrayAfterRender = false;
        Hide();
        ShowInTaskbar = false;
        Opacity = 1;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        await TryAutoLoginAsync();
    }

    private async Task CheckForUpdateAsync(bool notifyResult = false)
    {
        if (_updateCheckRunning || _updateBusy)
        {
            return;
        }

        _updateCheckRunning = true;
        try
        {
            AppUpdateInfo? update = await UpdateChecker.CheckAsync();
            await Dispatcher.InvokeAsync(() => ApplyUpdateCheckResult(update, notifyResult));
        }
        catch
        {
            if (notifyResult)
            {
                await Dispatcher.InvokeAsync(() => Toast.Warning(this, "업데이트를 확인하지 못했습니다. 인터넷 연결을 확인한 뒤 다시 시도해 주세요.", "업데이트 확인"));
            }
        }
        finally
        {
            _updateCheckRunning = false;
        }
    }

    private void ApplyUpdateCheckResult(AppUpdateInfo? update, bool notifyResult)
    {
        if (update != null)
        {
            ShowUpdateLink(update);
            return;
        }

        HideUpdateLink();
        if (notifyResult)
        {
            Toast.Success(this, "지금 쓰는 버전이 최신입니다.", "업데이트 확인");
        }
    }

    private void ShowUpdateLink(AppUpdateInfo update)
    {
        _availableUpdate = update;
        _updateLinkText = "새 버전이 있습니다. (v" + update.LatestLabel + ")";
        RestoreUpdateLink();
        UpdateLink.Visibility = Visibility.Visible;
    }

    private void HideUpdateLink()
    {
        _availableUpdate = null;
        _updateLinkText = string.Empty;
        UpdateLink.Text = string.Empty;
        UpdateLink.Visibility = Visibility.Collapsed;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdateAsync(notifyResult: true);
    }

    private async void UpdateLink_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_updateBusy || _availableUpdate == null)
        {
            return;
        }

        AppUpdateInfo update = _availableUpdate;
        if (!ShowConfirm(
                "업데이트",
                "아맛다보고서 v" + update.LatestLabel + "를 설치할까요?",
                FormatUpdateDetail(update), showSilentUpdate: true))
        {
            return;
        }

        bool silent = ConfirmDialogHost.SilentUpdateSelected;
        _updateBusy = true;
        UpdateLink.Text = "업데이트 받는 중...";
        UpdateLink.TextDecorations = null;
        UpdateLink.Cursor = Cursors.Arrow;

        try
        {
            string installerPath = await UpdateChecker.DownloadInstallerAsync(update);
            string executablePath = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
            UpdateChecker.LaunchInstaller(installerPath, silent, executablePath);
            _forceClose = true;
            Application.Current.Shutdown();
        }
        catch (Win32Exception)
        {
            RestoreUpdateLink();
        }
        catch (Exception exception)
        {
            RestoreUpdateLink();
            Toast.Warning(this, "업데이트를 받지 못했습니다. " + exception.Message, "업데이트");
        }
        finally
        {
            _updateBusy = false;
        }
    }

    private void RestoreUpdateLink()
    {
        UpdateLink.Text = _updateLinkText;
        UpdateLink.TextDecorations = TextDecorations.Underline;
        UpdateLink.Cursor = Cursors.Hand;
    }

    private async Task TryAutoLoginAsync()
    {
        if (RootFrame.Content is not LoginPage loginPage)
        {
            return;
        }

        SavedLogin? saved = AppRuntime.Current.Settings.AutoLoginEnabled
            ? CredentialStore.Load()
            : null;
        if (saved != null)
        {
            loginPage.ViewModel.LoginId = saved.LoginId;
        }

        bool networkReady = await loginPage.EnsureNetworkAsync();
        if (!networkReady || !AppRuntime.Current.Settings.AutoLoginEnabled || saved == null)
        {
            return;
        }

        loginPage.ViewModel.StatusMessage = "자동 로그인 중...";
        loginPage.ViewModel.IsBusy = true;

        bool restored = await AppRuntime.Current.TryRestoreSessionAsync();
        if (RootFrame.Content is LoginPage busyPage)
        {
            busyPage.ViewModel.IsBusy = false;
            busyPage.ViewModel.StatusMessage = string.Empty;
        }

        if (!restored || AppRuntime.Current.Session == null)
        {
            return;
        }

        NavigateToDashboard(AppRuntime.Current.Session);
    }

    public void NavigateToLogin()
    {
        _mailView?.Dispose(); _mailView = null; MailHost.Content = null;
        _manualView?.Dispose(); _manualView = null; ManualHost.Content = null;
        _content = ShellContent.Dashboard;
        Toast.DismissAll();
        AppRuntime.Current.ClearLogin();
        SidePanel.Visibility = Visibility.Collapsed;
        ApplyLoginChrome();
        SettingsOverlay.Visibility = Visibility.Collapsed;
        UpdateContentVisibility();
        RootFrame.Navigate(new LoginPage());
        ClearHistory();
    }

    public void NavigateToDashboard(AmaranthSession session)
    {
        _content = ShellContent.Dashboard;
        SidePanel.Visibility = Visibility.Visible;
        ApplyShellChrome();
        SettingsOverlay.Visibility = Visibility.Collapsed;
        UpdateContentVisibility();
        SetActiveNav(NavDashboard);
        DashboardPage page = new(session);
        AppRuntime.Current.Dashboard = page.ViewModel;
        page.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DashboardViewModel.TotalCount)) UpdateDashboardBadge(page.ViewModel.TotalCount);
        };
        UpdateDashboardBadge(page.ViewModel.TotalCount);
        RootFrame.Navigate(page);
        ClearHistory();
    }

    public void ShowFromTray()
    {
        _hideToTrayAfterRender = false;
        App.ConsumeInitialNotificationSuppression();
        Opacity = 1;
        ShowInTaskbar = true;
        Visibility = Visibility.Visible;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void InitializeTray()
    {
        _tray.Text = "아맛다보고서";
        _tray.Visible = true;
        try
        {
            string? exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                _tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            }
        }
        catch
        {
            _tray.Icon = System.Drawing.SystemIcons.Application;
        }

        _tray.BalloonTipClicked += (_, _) => Dispatcher.Invoke(ShowFromTray);
        WindowsNotification.TrayBalloon = ShowTrayBalloon;

        _tray.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                Dispatcher.Invoke(ShowFromTray);
            }
            else if (e.Button == Forms.MouseButtons.Right)
            {
                Dispatcher.Invoke(ShowTrayMenu);
            }
        };
    }

    private void ShowTrayBalloon(string title, string text)
    {
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = text;
        _tray.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _tray.ShowBalloonTip(8000);
    }

    private void ShowTrayMenu()
    {
        _trayMenu?.Close();
        TrayMenuWindow menu = new();
        _trayMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_trayMenu, menu))
            {
                _trayMenu = null;
            }
        };
        menu.ShowGuiRequested += ShowFromTray;
        menu.ExitRequested += ConfirmAndExit;
        menu.Show();
        menu.PositionNearTray();
        ScreenPlacement.StealFocus(menu);
    }

    private void ConfirmAndExit()
    {
        ShowFromTray();
        if (!ShowConfirm("종료하기", "정말로 종료하시겠습니까?"))
        {
            return;
        }

        _forceClose = true;
        Close();
    }

    internal bool ShowConfirm(string title, string message, string? detail = null, bool showSilentUpdate = false)
    {
        bool confirmed = false;
        DispatcherFrame frame = new();
        ConfirmDialogHost.Prepare(title, message, result =>
        {
            confirmed = result;
            ConfirmOverlay.Visibility = Visibility.Collapsed;
            UpdateContentVisibility();
            frame.Continue = false;
        }, detail, showSilentUpdate);
        ConfirmOverlay.Visibility = Visibility.Visible;
        UpdateContentVisibility();
        ConfirmOverlay.Focus();
        ConfirmDialogHost.Focus();
        Dispatcher.PushFrame(frame);
        return confirmed;
    }

    private static string FormatUpdateDetail(AppUpdateInfo update)
    {
        StringBuilder text = new();
        text.Append(AppVersion.Number).Append(" → ").Append(update.LatestLabel);
        if (update.ChangeNotes.Count > 0)
        {
            text.AppendLine().AppendLine();
            foreach (string note in update.ChangeNotes)
            {
                text.Append("· ").AppendLine(note);
            }
        }

        text.AppendLine().Append("설치를 시작하면 프로그램이 종료됩니다.");
        return text.ToString().TrimEnd();
    }

    private void ConfirmOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == ConfirmOverlay)
        {
            ConfirmDialogHost.Complete(false);
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose)
        {
            _mailView?.Dispose(); _mailView = null;
            _manualView?.Dispose(); _manualView = null;
            _trayMenu?.Close();
            _tray.Visible = false;
            _tray.Dispose();
            return;
        }

        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
    }

    private void EmailList_Click(object sender, RoutedEventArgs e)
    {
        AmaranthSession? session = AppRuntime.Current.Session;
        if (session == null) return;
        if (_mailView == null)
        {
            _mailView = new MailView(App.Client, session);
            _mailView.DashboardRequested += ShowDashboard;
            _mailView.UnreadCountChanged += UpdateMailBadge;
            MailHost.Content = _mailView;
        }
        _content = ShellContent.Mail;
        LeaveSettings();
        SetActiveNav(NavMail);
        UpdateContentVisibility();
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e) => ShowDashboard();

    private void ShowDashboard()
    {
        _content = ShellContent.Dashboard;
        LeaveSettings();
        SetActiveNav(NavDashboard);
        UpdateContentVisibility();
    }

    private void UpdateContentVisibility()
    {
        RootFrame.Visibility = _content == ShellContent.Dashboard ? Visibility.Visible : Visibility.Collapsed;
        // WebView2는 HWND를 사용하므로 WPF 설정·확인 창을 가리지 않도록 숨깁니다.
        bool covered = SettingsOverlay.Visibility == Visibility.Visible || ConfirmOverlay.Visibility == Visibility.Visible;
        MailHost.Visibility = _content == ShellContent.Mail && !covered ? Visibility.Visible : Visibility.Collapsed;
        ManualHost.Visibility = _content == ShellContent.Manual && !covered ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>사용자 매뉴얼을 새 창 대신 메인 창 안에 엽니다. 다른 메뉴로 갔다 와도 읽던 위치가 유지됩니다.</summary>
    private void Manual_Click(object sender, RoutedEventArgs e)
    {
        if (_manualView == null)
        {
            string folder = ManualView.DefaultFolder;
            if (!ManualView.Exists(folder))
            {
                // 이동하지 않으므로 원래 메뉴 선택을 되돌립니다.
                SetActiveNav(CurrentNav());
                Toast.Warning(this, "사용 매뉴얼 파일(ReadMe.html)을 찾을 수 없습니다. 프로그램이 있는 폴더를 확인해 주세요.", "사용자 매뉴얼");
                return;
            }

            _manualView = new ManualView(folder);
            ManualHost.Content = _manualView;
        }

        _content = ShellContent.Manual;
        LeaveSettings();
        SetActiveNav(NavManual);
        UpdateContentVisibility();
    }

    private RadioButton CurrentNav() =>
        SettingsOverlay.Visibility == Visibility.Visible ? NavSettings
        : _content switch { ShellContent.Mail => NavMail, ShellContent.Manual => NavManual, _ => NavDashboard };

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Visible;
        SetActiveNav(NavSettings);
        UpdateContentVisibility();
    }

    /// <summary>미작성 목록 배너의 '알림 설정' 버튼에서 호출합니다.</summary>
    public void OpenSettings() => Settings_Click(this, new RoutedEventArgs());

    /// <summary>환경설정에서 다른 메뉴로 이동할 때 입력 중인 값을 반영하고 새로고침 타이머를 다시 시작합니다.</summary>
    private void LeaveSettings()
    {
        if (SettingsOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        BindingOperations.GetBindingExpression(NotificationIntervalBox, TextBox.TextProperty)?.UpdateSource();
        SettingsOverlay.Visibility = Visibility.Collapsed;
        AppRuntime.Current.RestartRefreshTimer();
    }

    private void SetActiveNav(RadioButton target)
    {
        NavDashboard.IsChecked = ReferenceEquals(target, NavDashboard);
        NavMail.IsChecked = ReferenceEquals(target, NavMail);
        NavManual.IsChecked = ReferenceEquals(target, NavManual);
        NavSettings.IsChecked = ReferenceEquals(target, NavSettings);
    }

    private void UpdateDashboardBadge(int count)
    {
        DashboardBadgeText.Text = count > 99 ? "99+" : count.ToString();
        DashboardBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateMailBadge(int unread)
    {
        MailBadgeText.Text = unread > 99 ? "99+" : unread.ToString();
        MailBadge.Visibility = unread > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>로그인 화면: 사이드바 없이 창 전체를 바탕색으로 씁니다.</summary>
    private void ApplyLoginChrome()
    {
        ContentPanel.Margin = new Thickness(0);
        ContentPanel.BorderThickness = new Thickness(0);
        ContentPanel.CornerRadius = new CornerRadius(0);
        ContentPanel.Background = (System.Windows.Media.Brush)FindResource("BgBrush");
        UpdateMailBadge(0);
        UpdateDashboardBadge(0);
    }

    /// <summary>로그인 후: 사이드바 오른쪽에 흰색 라운드 패널을 띄웁니다.</summary>
    private void ApplyShellChrome()
    {
        ContentPanel.Margin = new Thickness(0, 12, 12, 12);
        ContentPanel.BorderThickness = new Thickness(1);
        ContentPanel.CornerRadius = new CornerRadius(16);
        ContentPanel.Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush");
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        LeaveSettings();
        UpdateContentVisibility();
    }

    private void NotificationInterval_LostFocus(object sender, RoutedEventArgs e)
    {
        BindingOperations.GetBindingExpression(NotificationIntervalBox, TextBox.TextProperty)?.UpdateSource();
        AppRuntime.Current.RestartRefreshTimer();
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        AppRuntime.Current.ForgetSavedLogin();
        NavigateToLogin();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        ConfirmAndExit();
    }

    private void DigitsOnly(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
    }

    private void DigitsOnlyPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        string text = Convert.ToString(e.DataObject.GetData(DataFormats.Text)) ?? string.Empty;
        if (!Regex.IsMatch(text, "^[0-9]+$"))
        {
            e.CancelCommand();
        }
    }

    private void ClearHistory()
    {
        if (RootFrame.NavigationService == null)
        {
            return;
        }

        while (RootFrame.NavigationService.CanGoBack)
        {
            RootFrame.NavigationService.RemoveBackEntry();
        }
    }
}
