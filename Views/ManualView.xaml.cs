using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Amaranth10API.Views;

/// <summary>
/// 사이드바의 "사용자 매뉴얼"로 메인 창 안에 표시하는 매뉴얼 화면.
/// 프로그램 폴더의 ReadMe.html(빌드 시 docs/사용매뉴얼.html에서 복사)을 내장 웹뷰어로 엽니다.
/// </summary>
public partial class ManualView : UserControl, IDisposable
{
    private const string HostName = "app.manual";
    private const string FileName = "ReadMe.html";
    private readonly string _folder;
    private readonly WebView2 _browser = new();
    private bool _closed;

    public ManualView(string folder)
    {
        _folder = folder;
        InitializeComponent();
        BrowserHost.Children.Add(_browser);
        Loaded += FirstLoad;
    }

    /// <summary>매뉴얼 파일이 있는 프로그램 폴더</summary>
    public static string DefaultFolder => AppDomain.CurrentDomain.BaseDirectory;

    public static bool Exists(string folder) => File.Exists(Path.Combine(folder, FileName));

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        Loaded -= FirstLoad;
        _browser.Dispose();
    }

    private async void FirstLoad(object sender, RoutedEventArgs e)
    {
        Loaded -= FirstLoad;
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "아맛다보고서",
                "WebView2Manual");
            Directory.CreateDirectory(userData);

            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userData);
            if (_closed) return;
            await _browser.EnsureCoreWebView2Async(environment);
            if (_closed) return;

            CoreWebView2 core = _browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsZoomControlEnabled = true;
            core.SetVirtualHostNameToFolderMapping(HostName, _folder, CoreWebView2HostResourceAccessKind.Allow);

            // 매뉴얼 밖으로 나가는 링크는 기본 브라우저로 엽니다.
            core.NavigationStarting += (_, args) =>
            {
                if (IsManualUri(args.Uri)) return;
                args.Cancel = true;
                if (args.IsUserInitiated) OpenExternal(args.Uri);
            };
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (args.IsUserInitiated) OpenExternal(args.Uri);
            };
            core.NavigationCompleted += (_, args) =>
            {
                if (_closed) return;
                if (!args.IsSuccess && LoadingOverlay.Visibility == Visibility.Visible)
                {
                    ShowError("사용 매뉴얼을 열지 못했습니다.");
                    return;
                }

                LoadingOverlay.Visibility = Visibility.Collapsed;
            };
            core.Navigate($"https://{HostName}/{FileName}");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowError("매뉴얼을 보려면 Microsoft Edge WebView2 Runtime이 필요합니다.");
        }
        catch (Exception exception)
        {
            if (!_closed) ShowError(exception.Message);
        }
    }

    private void ShowError(string message)
    {
        LoadingBar.Visibility = Visibility.Collapsed;
        LoadingText.Text = message;
    }

    private static bool IsManualUri(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed)
        && (string.Equals(parsed.Host, HostName, StringComparison.OrdinalIgnoreCase) || parsed.Scheme == "about");

    private static void OpenExternal(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)) return;
        try { Process.Start(new ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true }); }
        catch { /* 기본 브라우저가 없으면 무시 */ }
    }
}
