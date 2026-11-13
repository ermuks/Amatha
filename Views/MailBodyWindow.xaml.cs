using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using Amaranth10API.Helpers;
using Amaranth10API.Models;
using Amaranth10API.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Amaranth10API.Views;

public partial class MailBodyWindow : Window
{
    private const string DocumentUrl = "https://mail-body.invalid/message";
    private static Task<CoreWebView2Environment>? _environment;
    private readonly AmaranthClient _client;
    private readonly AmaranthSession _session;
    private readonly InboxMail _mail;
    private readonly WebView2 _browser = new();
    private readonly CancellationTokenSource _lifetime = new();
    private MailBody? _body;
    private bool _closed, _loading, _images, _openingDocument;

    public MailBodyWindow(AmaranthClient client, AmaranthSession session, InboxMail mail)
    {
        _client = client; _session = session; _mail = mail;
        InitializeComponent();
        SubjectText.Text = mail.Subject;
        BrowserHost.Children.Add(_browser);
        Loaded += FirstLoad;
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); _browser.Dispose(); _lifetime.Dispose(); _body = null; };
    }

    private async void FirstLoad(object sender, RoutedEventArgs e) { Loaded -= FirstLoad; await LoadAsync(); }
    private async void Retry_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    private void Images_Click(object sender, RoutedEventArgs e)
    {
        _images = true;
        ImagesButton.IsEnabled = false;
        StatusText.Text = "외부 이미지를 표시합니다.";
        NavigateBody();
    }

    private async Task LoadAsync()
    {
        if (_closed || _loading) return;
        _loading = true; RetryButton.IsEnabled = false; ImagesButton.IsEnabled = false;
        StatusText.Text = "본문을 불러오는 중입니다…";
        try
        {
            var body = await _client.GetMailBodyAsync(_session, _mail.Id, _lifetime.Token);
            if (_closed) return;
            await InitializeBrowserAsync();
            if (_closed) return;
            _body = body;
            SubjectText.Text = string.IsNullOrWhiteSpace(body.Subject) ? _mail.Subject : body.Subject;
            FromText.Text = "보낸 사람  " + body.From;
            ToText.Text = "받는 사람  " + body.To;
            DateText.Text = body.Date;
            AttachmentText.Text = $"첨부파일 {body.AttachmentCount}개 · 다운로드는 ERP에서 이용해 주세요.";
            AttachmentText.Visibility = body.AttachmentCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = _images ? "외부 이미지를 표시합니다." : "외부 이미지는 ‘이미지 표시’를 누르면 불러옵니다.";
            ImagesButton.IsEnabled = !_images;
            NavigateBody();
        }
        catch (OperationCanceledException) { if (!_closed) StatusText.Text = "조회 시간이 초과되었습니다. 다시 불러오기를 눌러 주세요."; }
        catch (Exception) { if (!_closed) StatusText.Text = "본문을 열지 못했습니다. 로그인 상태와 WebView2 설치를 확인한 뒤 다시 시도해 주세요."; }
        finally { _loading = false; if (!_closed) RetryButton.IsEnabled = true; }
    }

    private async Task InitializeBrowserAsync()
    {
        if (_browser.CoreWebView2 != null) return;
        // ERP 로그인 쿠키를 주입하지 않는 메일 전용 프로필입니다.
        if (_environment == null || _environment.IsFaulted)
            _environment = CoreWebView2Environment.CreateAsync(null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "아맛다보고서", "WebView2Mail"));
        var environment = await _environment;
        if (_closed) return;
        await _browser.EnsureCoreWebView2Async(environment);
        if (_closed) return;
        var core = _browser.CoreWebView2 ?? throw new InvalidOperationException("웹뷰어 초기화 실패");
        core.Settings.IsScriptEnabled = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            if (e.Request.Uri == DocumentUrl && e.ResourceContext == CoreWebView2WebResourceContext.Document && _body != null)
            {
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(MailBodyHtml.Create(_body)));
                e.Response = environment.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Security-Policy: " + MailBodyHtml.ContentPolicy(_images));
            }
            else if (!(_images && e.ResourceContext == CoreWebView2WebResourceContext.Image && e.Request.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                e.Response = environment.CreateWebResourceResponse(null, 403, "Blocked", "");
        };
        core.NavigationStarting += (_, e) =>
        {
            if (_openingDocument && e.Uri == DocumentUrl) { _openingDocument = false; return; }
            e.Cancel = true;
            if (e.IsUserInitiated) OpenLink(e.Uri);
        };
        core.FrameNavigationStarting += (_, e) => { e.Cancel = true; };
        core.NewWindowRequested += (_, e) => { e.Handled = true; if (e.IsUserInitiated) OpenLink(e.Uri); };
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.NavigationCompleted += (_, e) => { if (!_closed && !e.IsSuccess) StatusText.Text = "본문 표시가 중단되었습니다. 다시 불러오기를 눌러 주세요."; };
    }

    private void NavigateBody()
    {
        if (_body == null || _closed) return;
        _openingDocument = true;
        _browser.CoreWebView2.Navigate(DocumentUrl);
    }

    private void OpenLink(string url)
    {
        if (!MailBodyHtml.IsWebLink(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { StatusText.Text = "링크를 열지 못했습니다."; }
    }
}
