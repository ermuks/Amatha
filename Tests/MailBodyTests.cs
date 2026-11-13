using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using Amaranth10API.Helpers;
using Amaranth10API.Models;
using Amaranth10API.Services;
using Amaranth10API.Views;

namespace Amaranth10API.Tests;
internal static class MailBodyTests
{
    private static string Fixture(string html = "<table><tr><td>본문 테스트</td></tr></table><script>window.injected=true</script>") => JsonSerializer.Serialize(new {
        resultCode=0, resultData=new { code="0", muid=123, mime=new { body=new { html, plain="일반 본문 <텍스트>" }, fileList=new object[0] },
        decodeMime=new { subject="메일 제목", from="Sender &lt;sender@example.com&gt;", to="받는 사람", date="2026-11-13 06:56", email="test@example.com", empSeq="77" } } });
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Handler : HttpMessageHandler
    {
        public string Body = ""; public string Path = "";
        public bool WithAttachments;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Body = await request.Content!.ReadAsStringAsync(); Path = request.RequestUri!.AbsolutePath;
            if (Path == "/mail/mail003A01")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"resultCode\":0,\"resultData\":{\"code\":\"0\",\"Result\":\"OK\",\"TotalRecordCount\":1,\"TotalUnseenCount\":1,\"Records\":[{\"muid\":123,\"subject\":\"메일 제목\",\"fromAddrName\":\"담당자\",\"fromAddrEmail\":\"sender@example.com\",\"tooltipDate\":\"2026-11-13 06:56\",\"seen\":0,\"attach\":true}]}}") };
            if (Path == "/mail/mail002A37")
            {
                await Task.Delay(100, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) };
            }
            string response = WithAttachments ? Fixture("<table><tr><td>본문 테스트</td></tr></table><img id='remote-image' src='https://mail-images.example.test/pixel.png'><script>window.injected=true</script>") : Fixture();
            if (WithAttachments) response = response.Replace("\"fileList\":[]", "\"fileList\":[{\"originalFileName\":\"검증용 도면.pdf\",\"fileSize\":\"2048\",\"fileSn\":\"test-file\"},{\"originalFileName\":\"작업 안내.txt\",\"fileSize\":\"1024\",\"fileSn\":\"second-file\"}]");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }
    private static AmaranthClient Client(Handler handler)
    {
        var client = new AmaranthClient();
        typeof(AmaranthClient).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, new HttpClient(handler));
        return client;
    }
    private static AmaranthSession Session() => new() { AuthToken="fixture", HashKey="fixture-key" };
    public static void Verify()
    {
        var handler = new Handler(); var client = Client(handler);
        var body = client.GetMailBodyAsync(Session(), 123).GetAwaiter().GetResult();
        Check(handler.Path == "/mail/mail002A01" && handler.Body == "{\"uid\":\"123\"}", "상세 API와 UID 문자열 매핑");
        string attached = Fixture().Replace("\"fileList\":[]", "\"fileList\":[{\"originalFileName\":\"도면 &amp; 부품.pdf\",\"fileSize\":\"2048\",\"fileSn\":\"file-1\",\"fileExtsn\":\"pdf\"}]");
        var parsed = AmaranthClient.ParseMailBodyResponse(attached,123);
        Check(parsed.MailboxEmail == "test@example.com" && parsed.EmployeeSequence == "77" && parsed.Attachments[0].SizeBytes == 2048, "다운로드 계정과 크기 매핑");
        Check(parsed.AttachmentCount == 1 && parsed.Attachments[0].FileName == "도면 & 부품.pdf" && parsed.Attachments[0].SizeText == "2 KB" && parsed.Attachments[0].FileId == "file-1", "첨부파일 목록 매핑");
        Check(body.Subject == "메일 제목" && body.From == "Sender <sender@example.com>" && body.Html.Contains("<table>"), "디코딩된 헤더와 원문 HTML");
        Check(MailBodyHtml.Create(AmaranthClient.ParseMailBodyResponse(Fixture(""),123)).Contains("&lt;텍스트&gt;"), "일반 텍스트는 이스케이프해야 합니다.");
        Check(MailBodyHtml.Create(new MailBody()).Contains("본문이 없는"), "빈 본문 안내");
        foreach (string json in new[] { "{}", Fixture().Replace("\"code\":\"0\"", "\"code\":\"1\""), Fixture().Replace("\"muid\":123", "\"muid\":124"), Fixture().Replace("\"mime\":", "\"missing\":") })
        {
            bool rejected=false; try { AmaranthClient.ParseMailBodyResponse(json,123); } catch(InvalidOperationException) {rejected=true;}
            Check(rejected,"오류 또는 다른 UID의 본문을 표시하면 안 됩니다.");
        }
        Check(MailBodyHtml.ContentPolicy().Contains("img-src data: https: http:") && MailBodyHtml.ContentPolicy().Contains("script-src 'none'"),"외부 이미지 자동 허용 정책");
        Check(MailBodyHtml.IsWebLink("https://example.com") && !MailBodyHtml.IsWebLink("file:///C:/test") && !MailBodyHtml.IsWebLink("javascript:alert(1)"),"외부 링크 프로토콜 제한");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool stopped=false; try {client.GetMailBodyAsync(Session(),123,cancelled.Token).GetAwaiter().GetResult();} catch(OperationCanceledException){stopped=true;}
        Check(stopped,"본문 조회 취소");
    }
    [STAThread]
    public static int Smoke()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var appXml = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "App.xaml"));
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var styleXml = appXml.Descendants().Single(x => (string?)x.Attribute(xaml + "Key") == "QuietButtonStyle");
        app.Resources["QuietButtonStyle"] = (Style)System.Windows.Markup.XamlReader.Parse(styleXml.ToString());
        app.Dispatcher.BeginInvoke(new Action(async () => {
            MailBodyView? window = null; MailView? mailView = null; Window? hostWindow = null; int result=1;
            try
            {
                string profile=Path.Combine(AppContext.BaseDirectory,"test-data","mail-webview-"+Guid.NewGuid().ToString("N"));
                typeof(MailBodyView).GetField("_environment",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,CoreWebView2Environment.CreateAsync(null,profile));
                var handler = new Handler { WithAttachments = true };
                var client = Client(handler);
                typeof(AmaranthClient).GetField("_attachmentHttpClient",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(client,new HttpClient(handler));
                mailView = new MailView(client, Session());
                hostWindow = new Window { Content = mailView, Width = 900, Height = 760, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                hostWindow.Show();
                var inbox = (DataGrid)mailView.FindName("MailGrid");
                for (int i = 0; i < 50 && inbox.Items.Count == 0; i++) await Task.Delay(100);
                Check(inbox.Items.Count == 1 && inbox.Columns[1].ActualWidth > 250, "메인 창 메일 목록 로드와 목록 행 폭");
                inbox.SelectedItem = inbox.Items[0];
                typeof(MailView).GetMethod("OpenMail", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(mailView, new object[] { inbox.SelectedItem });
                window = (MailBodyView)((ContentControl)mailView.FindName("BodyHost")).Content;
                var browser=(WebView2)typeof(MailBodyView).GetField("_browser",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
                browser.CoreWebView2InitializationCompleted += (_, _) =>
                {
                    browser.CoreWebView2.WebResourceRequested += (_, e) =>
                    {
                        if (e.Request.Uri == "https://mail-images.example.test/pixel.png")
                        {
                            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWQAAAABJRU5ErkJggg==");
                            e.Response = browser.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(png),200,"OK","Content-Type: image/png");
                        }
                    };
                };
                bool ready=false;
                for(int i=0;i<150;i++)
                {
                    await Task.Delay(100);
                    if(browser.CoreWebView2 != null && await browser.CoreWebView2.ExecuteScriptAsync("document.body && document.body.innerText.includes('본문 테스트') && document.getElementById('remote-image')?.naturalWidth === 1") == "true") {ready=true;break;}
                }
                Check(ready,"실제 WebView2 본문·외부 이미지 자동 표시 시간 초과");
                var list = (ItemsControl)window.FindName("AttachmentList");
                Check(list.Items.Count == 2 && list.IsEnabled && ((FrameworkElement)window.FindName("AttachmentPanel")).Visibility == Visibility.Visible, "첨부 목록 표시");
                var allButton = (Button)window.FindName("DownloadAllButton");
                Check(allButton.Visibility == Visibility.Visible && allButton.IsEnabled, "전체 다운로드 버튼 표시");
                string folder = Path.Combine(AppContext.BaseDirectory, "test-data", "bulk-ui-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                var download = (Task)typeof(MailBodyView).GetMethod("DownloadAllToFolderAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, new object[] { folder })!;
                Check(!allButton.IsEnabled && !list.IsEnabled && ((Button)window.FindName("CancelDownloadButton")).Visibility == Visibility.Visible, "다운로드 중 중복 실행 차단·취소 표시");
                Check(!inbox.IsEnabled, "다운로드 중에는 목록에서 다른 메일로 넘어가지 못해야 함");
                await download;
                await Task.Delay(100);
                Check(inbox.IsEnabled, "다운로드가 끝나면 목록을 다시 쓸 수 있어야 함");
                Check(Directory.GetFiles(folder).Length == 2 && allButton.IsEnabled && list.IsEnabled
                    && ((TextBlock)window.FindName("DownloadStatusText")).Text.Contains("2개 저장")
                    && ((Button)window.FindName("CancelDownloadButton")).Visibility == Visibility.Collapsed, "전체 저장 완료 및 UI 복원");
                var panel = (FrameworkElement)mailView.Content;   // 목록 + 본문 2단 전체
                panel.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)panel.ActualWidth, (int)panel.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                var visual = new System.Windows.Media.DrawingVisual();
                using (var draw = visual.RenderOpen())
                {
                    var bounds = new Rect(0, 0, panel.ActualWidth, panel.ActualHeight);
                    draw.DrawRectangle(window.Background, null, bounds);
                    draw.DrawRectangle(new System.Windows.Media.VisualBrush(panel), null, bounds);
                }
                bitmap.Render(visual);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var preview = File.Create(Path.Combine(AppContext.BaseDirectory,"test-data","mail-attachments-preview.png"))) encoder.Save(preview);
                Check(await browser.CoreWebView2!.ExecuteScriptAsync("!!document.querySelector('table') && window.injected === undefined") == "true","원문 표 표시와 스크립트 비실행");
                Check(!browser.CoreWebView2.Settings.IsScriptEnabled && !browser.CoreWebView2.Settings.IsWebMessageEnabled,"메일 전용 웹뷰어 격리 설정");
                Check((await browser.CoreWebView2.CookieManager.GetCookiesAsync("https://erp.teia.co.kr")).Count == 0,"메일 웹뷰어에 ERP 쿠키가 없어야 합니다.");
                using(var file=File.Create(Path.Combine(AppContext.BaseDirectory,"test-data","mail-body-preview.png"))) await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
                ((Button)window.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Application.Current.Windows.Count == 1 && ((ContentControl)mailView.FindName("BodyHost")).Content == null
                    && inbox.SelectedIndex == 0 && ((FrameworkElement)mailView.FindName("EmptyBodyPanel")).Visibility == Visibility.Visible,
                    "실제 메일 목록·본문·닫기 중 창 추가 없음");
                Console.WriteLine("PASS 첨부 전체 다운로드·중복 실행 차단·완료 UI, 실제 WebView2 본문·표, 스크립트 차단, ERP 쿠키 격리"); result=0;
            }
            catch(Exception error){Console.WriteLine("FAIL mail WebView2: "+error.GetBaseException().Message);}
            finally {mailView?.Dispose();window?.Dispose();hostWindow?.Close();app.Shutdown(result);}
        }));
        return app.Run();
    }
}
