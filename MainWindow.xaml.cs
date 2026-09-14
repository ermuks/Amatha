using System.ComponentModel;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private string _updateLinkText = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = AppVersion.FooterLabel;
        SettingsOverlay.DataContext = AppRuntime.Current.Settings;
        InitializeTray();
        NavigateToLogin();
        Loaded += MainWindow_Loaded;
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

    private async Task CheckForUpdateAsync()
    {
        try
        {
            AppUpdateInfo? update = await UpdateChecker.CheckAsync();
            if (update == null)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() => ShowUpdateLink(update));
        }
        catch
        {
            // GitHub에 닿지 않아도 프로그램은 그대로 씁니다.
        }
    }

    private void ShowUpdateLink(AppUpdateInfo update)
    {
        _availableUpdate = update;
        _updateLinkText = "새 버전이 있습니다. (v" + update.LatestLabel + ")";
        UpdateLink.Text = _updateLinkText;
        UpdateLink.Visibility = Visibility.Visible;
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
                "아맛다보고서 v" + update.LatestLabel + "를 설치할까요?\n설치를 시작하면 프로그램이 종료됩니다."))
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
        if (!AppRuntime.Current.Settings.AutoLoginEnabled)
        {
            return;
        }

        if (RootFrame.Content is LoginPage loginPage)
        {
            SavedLogin? saved = CredentialStore.Load();
            if (saved != null)
            {
                loginPage.ViewModel.LoginId = saved.LoginId;
                loginPage.ViewModel.StatusMessage = "자동 로그인 중...";
                loginPage.ViewModel.IsBusy = true;
            }
        }

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
        if (!ShowConfirm("종료하기", "정말로 종료하시겠습니까?"))
        {
            return;
        }

        _forceClose = true;
        Close();
    }

    internal bool ShowConfirm(string title, string message)
    {
        bool confirmed = false;
        DispatcherFrame frame = new();
        ConfirmDialogHost.Prepare(title, message, result =>
        {
            confirmed = result;
            ConfirmOverlay.Visibility = Visibility.Collapsed;
            frame.Continue = false;
        });
        ConfirmOverlay.Visibility = Visibility.Visible;
        ConfirmOverlay.Focus();
        ConfirmDialogHost.Focus();
        Dispatcher.PushFrame(frame);
        return confirmed;
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
        SettingsOverlay.Visibility = Visibility.Collapsed;
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
