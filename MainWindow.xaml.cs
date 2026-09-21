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
using Amaranth10API.Views;
using Forms = System.Windows.Forms;

namespace Amaranth10API;

public partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon _tray = new();
    private TrayMenuWindow? _trayMenu;
    private bool _forceClose;
    private bool _menuExpanded;
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
        if (AppRuntime.Current.Settings.StartInTray && App.StartedFromWindowsStartup)
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
                await Dispatcher.InvokeAsync(() => MessageBox.Show(
                    this,
                    "업데이트를 확인하지 못했습니다.",
                    "아맛다보고서",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning));
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
            MessageBox.Show(
                this,
                "지금 쓰는 버전이 최신입니다.",
                "아맛다보고서",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
        CollapseMenu(animate: true);
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
                FormatUpdateDetail(update)))
        {
            return;
        }

        _updateBusy = true;
        UpdateLink.Text = "업데이트 받는 중...";
        UpdateLink.TextDecorations = null;
        UpdateLink.Cursor = Cursors.Arrow;

        try
        {
            string installerPath = await UpdateChecker.DownloadInstallerAsync(update);
            UpdateChecker.LaunchInstaller(installerPath);
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
            MessageBox.Show(
                this,
                "업데이트를 받지 못했습니다.\n" + exception.Message,
                "아맛다보고서",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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
        AppRuntime.Current.ClearLogin();
        SidePanel.Visibility = Visibility.Collapsed;
        CollapseMenu(animate: false);
        SettingsOverlay.Visibility = Visibility.Collapsed;
        RootFrame.Navigate(new LoginPage());
        ClearHistory();
    }

    public void NavigateToDashboard(AmaranthSession session)
    {
        SidePanel.Visibility = Visibility.Visible;
        SettingsOverlay.Visibility = Visibility.Collapsed;
        DashboardPage page = new(session);
        AppRuntime.Current.Dashboard = page.ViewModel;
        RootFrame.Navigate(page);
        ClearHistory();
    }

    public void ShowFromTray()
    {
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
        CollapseMenu(animate: true);
        if (!ShowConfirm("종료하기", "정말로 종료하시겠습니까?"))
        {
            return;
        }

        _forceClose = true;
        Close();
    }

    internal bool ShowConfirm(string title, string message, string? detail = null)
    {
        bool confirmed = false;
        DispatcherFrame frame = new();
        ConfirmDialogHost.Prepare(title, message, result =>
        {
            confirmed = result;
            ConfirmOverlay.Visibility = Visibility.Collapsed;
            frame.Continue = false;
        }, detail);
        ConfirmOverlay.Visibility = Visibility.Visible;
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
            _trayMenu?.Close();
            _tray.Visible = false;
            _tray.Dispose();
            return;
        }

        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
        CollapseMenu(animate: false);
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_menuExpanded)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && IsInside(source, SidePanel))
        {
            return;
        }

        CollapseMenu(animate: true);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _menuExpanded)
        {
            CollapseMenu(animate: true);
            e.Handled = true;
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_menuExpanded)
        {
            CollapseMenu(animate: true);
        }
    }

    private void Hamburger_Click(object sender, RoutedEventArgs e)
    {
        if (_menuExpanded)
        {
            CollapseMenu(animate: true);
        }
        else
        {
            ExpandMenu();
        }
    }

    private static bool IsInside(DependencyObject current, DependencyObject ancestor)
    {
        DependencyObject? node = current;
        while (node != null)
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }

            DependencyObject? parent = VisualTreeHelper.GetParent(node);
            if (parent == null && node is FrameworkElement element)
            {
                parent = element.Parent;
            }

            node = parent;
        }

        return false;
    }

    private void ExpandMenu()
    {
        _menuExpanded = true;
        SideMenuItems.Visibility = Visibility.Visible;
        AnimatePanel(240);
    }

    private void CollapseMenu(bool animate)
    {
        _menuExpanded = false;
        SideMenuItems.Visibility = Visibility.Collapsed;
        if (animate)
        {
            AnimatePanel(40);
        }
        else
        {
            SidePanel.BeginAnimation(WidthProperty, null);
            SidePanel.Width = 40;
        }
    }

    private void AnimatePanel(double width)
    {
        DoubleAnimation animation = new(width, TimeSpan.FromMilliseconds(180))
        {
            AccelerationRatio = 0.2,
            DecelerationRatio = 0.4
        };
        SidePanel.BeginAnimation(WidthProperty, animation);
    }

    private void Manual_Click(object sender, RoutedEventArgs e)
    {
        ManualWindow.Open(this);
        CollapseMenu(animate: true);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Visible;
        CollapseMenu(animate: true);
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        BindingOperations.GetBindingExpression(NotificationIntervalBox, TextBox.TextProperty)?.UpdateSource();
        SettingsOverlay.Visibility = Visibility.Collapsed;
        AppRuntime.Current.RestartRefreshTimer();
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
