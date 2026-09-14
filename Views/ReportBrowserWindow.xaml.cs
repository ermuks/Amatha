using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Amaranth10API.Helpers;
using Amaranth10API.Models;
using Amaranth10API.Services;
using Amaranth10API.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Amaranth10API.Views;

public partial class ReportBrowserWindow : Window
{
    private static readonly SemaphoreSlim EnvironmentLock = new(1, 1);
    private static CoreWebView2Environment? SharedEnvironment;

    private readonly AmaranthSession _session;
    private readonly string? _startUrl;
    private readonly bool _hostExternalNavigation;
    private readonly ReportDraftFill? _fill;
    private readonly WebView2 _browser = new();
    private Task<CoreWebView2>? _initializeTask;
    private bool _fillStarted;

    public ReportBrowserWindow(
        AmaranthSession session,
        string? startUrl = null,
        bool hostExternalNavigation = false,
        ReportDraftFill? fill = null)
    {
        _session = session;
        _startUrl = startUrl;
        _hostExternalNavigation = hostExternalNavigation;
        _fill = fill;
        InitializeComponent();
        ApplyWindowIcon();
        AddressBox.Text = _startUrl ?? AmaranthClient.CreateReportDraftUrl();
        BrowserHost.Children.Insert(0, _browser);
        PlaceOnScreen();
        Loaded += Window_Loaded;
        Closed += Window_Closed;
    }

    public static void OpenDraft(Window? owner, AmaranthSession session, ReportDraftFill? fill = null)
    {
        ReportBrowserWindow window = new(session, AmaranthClient.CreateReportDraftUrl(), fill: fill)
        {
            Owner = owner
        };
        window.Show();
    }

    public Task<CoreWebView2> InitializeCoreAsync()
    {
        return _initializeTask ??= InitializeCoreInternalAsync();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= Window_Loaded;
        try
        {
            CoreWebView2 core = await InitializeCoreAsync();
            if (!_hostExternalNavigation)
            {
                core.Navigate(_startUrl ?? AmaranthClient.CreateReportDraftUrl());
            }
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowLoadError("보고서 화면을 열려면 Microsoft Edge WebView2 Runtime이 필요합니다.");
        }
        catch (Exception exception)
        {
            ShowLoadError(exception.Message);
        }
    }

    private async Task<CoreWebView2> InitializeCoreInternalAsync()
    {
        CoreWebView2Environment environment = await GetEnvironmentAsync();
        await _browser.EnsureCoreWebView2Async(environment);

        CoreWebView2 core = _browser.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsZoomControlEnabled = true;
        core.SourceChanged += (_, _) => UpdateAddress();
        core.HistoryChanged += (_, _) => UpdateAddress();
        core.NavigationCompleted += Core_NavigationCompleted;
        core.NewWindowRequested += Core_NewWindowRequested;
        ApplySessionCookies(core);
        UpdateAddress();
        return core;
    }

    private void ApplySessionCookies(CoreWebView2 core)
    {
        CoreWebView2CookieManager manager = core.CookieManager;
        Dictionary<string, string> cookies = new(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, string> pair in App.Client.GetCookiePairs())
        {
            cookies[pair.Key] = pair.Value;
        }

        cookies["oAuthToken"] = _session.AuthToken;
        cookies["signKey"] = _session.HashKey;
        cookies["BIZCUBE_AT"] = _session.AuthToken;
        cookies["BIZCUBE_HK"] = _session.HashKey;
        cookies["BIZCUBE_TYPE"] = "WEB";
        cookies["al"] = "KR";

        foreach (KeyValuePair<string, string> pair in cookies)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
            {
                continue;
            }

            CoreWebView2Cookie cookie = manager.CreateCookie(pair.Key, pair.Value, "erp.teia.co.kr", "/");
            cookie.IsSecure = true;
            manager.AddOrUpdateCookie(cookie);
        }
    }

    private async void Core_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        CoreWebView2Deferral deferral = e.GetDeferral();
        e.Handled = true;
        try
        {
            ReportBrowserWindow child = new(_session, hostExternalNavigation: true)
            {
                Owner = this,
                Title = Title
            };

            if (e.WindowFeatures.HasSize)
            {
                child.Width = Math.Max(420, e.WindowFeatures.Width);
                child.Height = Math.Max(320, e.WindowFeatures.Height);
            }

            child.Show();
            e.NewWindow = await child.InitializeCoreAsync();
        }
        catch
        {
            // 팝업 연결에 실패해도 작성 창은 유지합니다.
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        UpdateAddress();
        if (!e.IsSuccess && LoadingOverlay.Visibility == Visibility.Visible)
        {
            ShowLoadError("보고서 작성 화면을 열지 못했습니다.");
            return;
        }

        LoadingOverlay.Visibility = Visibility.Collapsed;
        await TryFillDraftAsync();
    }

    private async Task TryFillDraftAsync()
    {
        if (_fillStarted || _hostExternalNavigation || _fill == null || _browser.CoreWebView2 == null)
        {
            return;
        }

        string? source = _browser.CoreWebView2.Source;
        if (string.IsNullOrWhiteSpace(source) ||
            source.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _fillStarted = true;
        try
        {
            await ReportDraftFiller.TryFillAsync(_browser.CoreWebView2, _fill);
        }
        catch
        {
            // 자동 입력 실패는 작성 창을 막지 않습니다.
        }
    }

    private async void UpdateAddress()
    {
        string? address = null;
        try
        {
            if (_browser.CoreWebView2 != null)
            {
                string scriptResult = await _browser.CoreWebView2.ExecuteScriptAsync("location.href");
                address = System.Text.Json.JsonSerializer.Deserialize<string>(scriptResult);
            }
        }
        catch
        {
            // location.href를 못 읽으면 Source로 대체합니다.
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            address = _browser.CoreWebView2?.Source;
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            address = _startUrl ?? AmaranthClient.CreateReportDraftUrl();
        }

        AddressBox.Text = address;
    }

    private void ShowLoadError(string message)
    {
        LoadingText.Text = message;
        LoadingOverlay.Visibility = Visibility.Visible;
    }

    private void ApplyWindowIcon()
    {
        try
        {
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIcon.ico", UriKind.Absolute));
        }
        catch
        {
            if (Application.Current?.MainWindow?.Icon != null)
            {
                Icon = Application.Current.MainWindow.Icon;
            }
        }
    }

    private void PlaceOnScreen()
    {
        Rect work = ScreenPlacement.WorkingAreaDip();
        Width = Math.Min(1020, Math.Max(720, work.Width - 32));
        Height = Math.Min(work.Height - 32, Math.Max(560, work.Height * 0.82));
        Left = work.Left + Math.Max(0, (work.Width - Width) / 2);
        Top = work.Top + Math.Max(0, (work.Height - Height) / 2);
        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        Closed -= Window_Closed;
        if (_hostExternalNavigation)
        {
            return;
        }

        DashboardViewModel? dashboard = AppRuntime.Current.Dashboard;
        if (dashboard == null)
        {
            return;
        }

        try
        {
            await dashboard.ReloadAsync(silent: true);
        }
        catch
        {
            // 작성 창을 닫은 뒤 목록 새로고침 실패는 무시합니다.
        }
    }

    private static async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        await EnvironmentLock.WaitAsync();
        try
        {
            if (SharedEnvironment != null)
            {
                return SharedEnvironment;
            }

            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "아맛다보고서",
                "WebView2");
            Directory.CreateDirectory(userData);
            SharedEnvironment = await CoreWebView2Environment.CreateAsync(null, userData);
            return SharedEnvironment;
        }
        finally
        {
            EnvironmentLock.Release();
        }
    }
}
