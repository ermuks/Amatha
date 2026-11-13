using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Amaranth10API.Helpers;
using Amaranth10API.Models;
using Amaranth10API.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Amaranth10API.Views;

public partial class MailBodyView : UserControl, IDisposable
{
    private const string DocumentUrl = "https://mail-body.invalid/message";
    private static Task<CoreWebView2Environment>? _environment;
    private readonly AmaranthClient _client;
    private readonly AmaranthSession _session;
    private InboxMail _mail;
    private readonly WebView2 _browser = new();
    private readonly CancellationTokenSource _lifetime = new();
    private MailBody? _body;
    private CancellationTokenSource? _downloadCts;
    private CancellationTokenSource? _loadCts;
    private int _loadVersion;
    private bool _closed, _loading, _openingDocument;
    public event Action? BackRequested;
    /// <summary>첨부파일 다운로드가 시작(true)·끝(false)될 때 알립니다. 목록은 그동안 다른 메일로 넘어가지 못하게 잠급니다.</summary>
    public event Action<bool>? DownloadingChanged;

    /// <summary>지금 보여 주는(또는 불러오는 중인) 메일의 UID</summary>
    public long MailId => _mail.Id;

    public MailBodyView(AmaranthClient client, AmaranthSession session, InboxMail mail)
    {
        _client = client; _session = session; _mail = mail;
        InitializeComponent();
        ShowHeader(mail);
        BrowserHost.Children.Add(_browser);
        Loaded += FirstLoad;
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true; Loaded -= FirstLoad;
        _lifetime.Cancel(); _browser.Dispose(); _lifetime.Dispose(); _body = null;
        BackRequested = null; DownloadingChanged = null;
    }

    /// <summary>
    /// 같은 웹뷰어로 다른 메일을 엽니다(2단 화면에서 목록 선택이 바뀔 때).
    /// 첨부파일을 받는 중이면 바꾸지 않고 false를 돌려줍니다.
    /// </summary>
    public bool ShowMail(InboxMail mail)
    {
        if (_closed || _downloadCts != null) return false;
        if (mail.Id == _mail.Id) return true;
        _mail = mail;
        _body = null;
        ShowHeader(mail);
        ToText.Text = string.Empty; DateText.Text = string.Empty;
        AttachmentList.ItemsSource = null;
        AttachmentPanel.Visibility = Visibility.Collapsed; AttachmentText.Visibility = Visibility.Collapsed;
        DownloadAllButton.Visibility = Visibility.Collapsed;
        DownloadStatusText.Text = string.Empty; DownloadStatusText.ToolTip = null;
        // 새 본문이 뜰 때까지 이전 메일 본문이 보이지 않게 가립니다.
        BrowserHost.Visibility = Visibility.Hidden;
        if (IsLoaded) _ = LoadAsync();   // 아직 화면에 붙기 전이면 FirstLoad가 새 메일을 불러옵니다.
        return true;
    }

    private void ShowHeader(InboxMail mail)
    {
        SubjectText.Text = mail.Subject;
        FromText.Text = mail.SenderName;
        AvatarText.Text = Initial(mail.SenderName);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private async void FirstLoad(object sender, RoutedEventArgs e) { Loaded -= FirstLoad; await LoadAsync(); }
    private async void Retry_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    private void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
        CancelDownloadButton.IsEnabled = false;
    }

    private async void DownloadAll_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _loading || _downloadCts != null || _body == null) return;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "모든 첨부파일을 저장할 폴더를 선택하세요. 같은 이름의 파일에는 번호를 붙입니다.",
            ShowNewFolderButton = true
        };
        var window = Window.GetWindow(this);
        if (window == null) return;
        var owner = new DialogOwner(new System.Windows.Interop.WindowInteropHelper(window).Handle);
        if (dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK && !_closed)
            await DownloadAllToFolderAsync(dialog.SelectedPath);
    }

    private async Task DownloadAllToFolderAsync(string folder)
    {
        if (_closed || _loading || _downloadCts != null || _body == null) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        BeginDownload(cancellation);
        var progress = new Progress<MailDownloadProgress>(item =>
        {
            if (!_closed && ReferenceEquals(_downloadCts, cancellation))
                DownloadStatusText.Text = $"전체 다운로드 {item.Index}/{item.Total} · {item.FileName} · {item.ReceivedBytes / 1024.0:N0} KB 받음";
        });
        try
        {
            var result = await _client.DownloadAllMailAttachmentsAsync(_session, _body, folder, progress, cancellation.Token);
            if (!_closed)
            {
                string status = result.Cancelled ? "전체 다운로드 취소" : "전체 다운로드 완료";
                DownloadStatusText.Text = $"{status} · {result.SavedCount}개 저장" +
                    (result.FailedFiles.Count > 0 ? $" · {result.FailedFiles.Count}개 실패 (개별 다운로드로 다시 시도해 주세요.)" : "");
                DownloadStatusText.ToolTip = result.FailedFiles.Count > 0 ? "실패한 파일:\n" + string.Join("\n", result.FailedFiles) : null;
            }
        }
        catch (Exception)
        {
            if (!_closed) DownloadStatusText.Text = "전체 다운로드를 시작하지 못했습니다. 저장 폴더를 확인하고 다시 시도해 주세요.";
        }
        finally { EndDownload(); }
    }

    private void BeginDownload(CancellationTokenSource cancellation)
    {
        _downloadCts = cancellation;
        AttachmentList.IsEnabled = false; RetryButton.IsEnabled = false; DownloadAllButton.IsEnabled = false;
        CancelDownloadButton.Visibility = Visibility.Visible; CancelDownloadButton.IsEnabled = true;
        DownloadStatusText.ToolTip = null;
        DownloadStatusText.Text = "다운로드를 시작합니다…";
        DownloadingChanged?.Invoke(true);
    }

    private void EndDownload()
    {
        _downloadCts = null;
        if (_closed) return;
        AttachmentList.IsEnabled = true; RetryButton.IsEnabled = true; DownloadAllButton.IsEnabled = true;
        CancelDownloadButton.Visibility = Visibility.Collapsed;
        DownloadingChanged?.Invoke(false);
    }

    private sealed class DialogOwner : System.Windows.Forms.IWin32Window
    {
        public DialogOwner(IntPtr handle) { Handle = handle; }
        public IntPtr Handle { get; }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _loading || _downloadCts != null || _body == null
            || sender is not FrameworkElement element || element.DataContext is not MailAttachment attachment) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "첨부파일 저장", FileName = MailAttachmentFile.SuggestedName(attachment.FileName),
            Filter = "모든 파일 (*.*)|*.*", AddExtension = false, CheckPathExists = true, OverwritePrompt = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true || _closed) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        BeginDownload(cancellation);
        var progress = new Progress<long>(received =>
        {
            if (!_closed && ReferenceEquals(_downloadCts, cancellation))
                DownloadStatusText.Text = $"다운로드 중 · {received / 1024.0:N0} KB 받음";
        });
        try
        {
            await _client.DownloadMailAttachmentAsync(_session, _body, attachment, dialog.FileName, progress, cancellation.Token);
            if (!_closed) DownloadStatusText.Text = "저장 완료 · " + Path.GetFileName(dialog.FileName);
        }
        catch (OperationCanceledException)
        {
            if (!_closed) DownloadStatusText.Text = cancellation.IsCancellationRequested ? "다운로드를 취소했습니다." : "다운로드 시간이 초과되었습니다. 다시 시도해 주세요.";
        }
        catch (Exception)
        {
            if (!_closed) DownloadStatusText.Text = "저장하지 못했습니다. 로그인 상태, 저장 폴더와 여유 공간을 확인하고 다시 시도해 주세요.";
        }
        finally { EndDownload(); }
    }
    private async Task LoadAsync()
    {
        if (_closed || _downloadCts != null) return;
        // 다른 메일을 고르면 이전 조회는 취소하고, 늦게 도착한 결과는 버립니다.
        _loadCts?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _loadCts = cancellation;
        int version = ++_loadVersion;
        InboxMail mail = _mail;
        bool Current() => !_closed && version == _loadVersion;
        _loading = true; RetryButton.IsEnabled = false;
        DownloadAllButton.IsEnabled = false;
        StatusText.Text = "본문을 불러오는 중입니다…";
        try
        {
            var body = await _client.GetMailBodyAsync(_session, mail.Id, cancellation.Token);
            if (!Current()) return;
            await InitializeBrowserAsync();
            if (!Current()) return;
            _body = body;
            SubjectText.Text = string.IsNullOrWhiteSpace(body.Subject) ? mail.Subject : body.Subject;
            FromText.Text = body.From;
            AvatarText.Text = Initial(body.From);
            ToText.Text = "받는 사람  " + System.Text.RegularExpressions.Regex.Replace(body.To, @"\s+", " ").Trim();
            DateText.Text = body.Date;
            AttachmentText.Text = $"첨부파일 {body.AttachmentCount}개";
            AttachmentList.ItemsSource = body.Attachments;
            AttachmentPanel.Visibility = body.AttachmentCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            AttachmentText.Visibility = body.AttachmentCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            DownloadAllButton.Visibility = body.AttachmentCount > 1 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = string.Empty;
            NavigateBody();
        }
        catch (OperationCanceledException) { if (Current()) StatusText.Text = "조회 시간이 초과되었습니다. 다시 불러오기를 눌러 주세요."; }
        catch (Exception) { if (Current()) StatusText.Text = "본문을 열지 못했습니다. 로그인 상태와 WebView2 설치를 확인한 뒤 다시 시도해 주세요."; }
        finally
        {
            if (ReferenceEquals(_loadCts, cancellation)) _loadCts = null;
            cancellation.Dispose();
            if (Current()) { _loading = false; RetryButton.IsEnabled = true; DownloadAllButton.IsEnabled = true; }
        }
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
                e.Response = environment.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Security-Policy: " + MailBodyHtml.ContentPolicy());
            }
            else if (!(e.ResourceContext == CoreWebView2WebResourceContext.Image && MailBodyHtml.IsWebLink(e.Request.Uri)))
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
        core.NavigationCompleted += (_, e) =>
        {
            if (_closed) return;
            if (e.IsSuccess) BrowserHost.Visibility = Visibility.Visible;
            else StatusText.Text = "본문 표시가 중단되었습니다. 다시 불러오기를 눌러 주세요.";
        };
    }

    /// <summary>보낸 사람 표시("이름 &lt;주소&gt;" 또는 주소)에서 아바타 첫 글자를 고릅니다.</summary>
    internal static string Initial(string from)
    {
        foreach (char c in from ?? string.Empty)
        {
            if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
        }

        return "?";
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
