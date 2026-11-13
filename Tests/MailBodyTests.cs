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
        decodeMime=new { subject="메일 제목", from="Sender &lt;sender@example.com&gt;", to="받는 사람", date="2026-11-13 06:56" } } });
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Handler : HttpMessageHandler
    {
        public string Body = ""; public string Path = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Body = await request.Content!.ReadAsStringAsync(); Path = request.RequestUri!.AbsolutePath;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixture()) };
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
        Check(body.Subject == "메일 제목" && body.From == "Sender <sender@example.com>" && body.Html.Contains("<table>"), "디코딩된 헤더와 원문 HTML");
        Check(MailBodyHtml.Create(AmaranthClient.ParseMailBodyResponse(Fixture(""),123)).Contains("&lt;텍스트&gt;"), "일반 텍스트는 이스케이프해야 합니다.");
        Check(MailBodyHtml.Create(new MailBody()).Contains("본문이 없는"), "빈 본문 안내");
        foreach (string json in new[] { "{}", Fixture().Replace("\"code\":\"0\"", "\"code\":\"1\""), Fixture().Replace("\"muid\":123", "\"muid\":124"), Fixture().Replace("\"mime\":", "\"missing\":") })
        {
            bool rejected=false; try { AmaranthClient.ParseMailBodyResponse(json,123); } catch(InvalidOperationException) {rejected=true;}
            Check(rejected,"오류 또는 다른 UID의 본문을 표시하면 안 됩니다.");
        }
        Check(!MailBodyHtml.ContentPolicy(false).Contains("https:") && MailBodyHtml.ContentPolicy(true).Contains("img-src data: https:"),"외부 이미지 정책");
        Check(MailBodyHtml.IsWebLink("https://example.com") && !MailBodyHtml.IsWebLink("file:///C:/test") && !MailBodyHtml.IsWebLink("javascript:alert(1)"),"외부 링크 프로토콜 제한");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool stopped=false; try {client.GetMailBodyAsync(Session(),123,cancelled.Token).GetAwaiter().GetResult();} catch(OperationCanceledException){stopped=true;}
        Check(stopped,"본문 조회 취소");
    }
    [STAThread]
    public static int Smoke()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources["QuietButtonStyle"] = new Style(typeof(Button));
        app.Dispatcher.BeginInvoke(new Action(async () => {
            MailBodyWindow? window = null; int result=1;
            try
            {
                string profile=Path.Combine(AppContext.BaseDirectory,"test-data","mail-webview-"+Guid.NewGuid().ToString("N"));
                typeof(MailBodyWindow).GetField("_environment",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,CoreWebView2Environment.CreateAsync(null,profile));
                window = new MailBodyWindow(Client(new Handler()),Session(),new InboxMail {Id=123,Subject="메일 제목"}) {Opacity=0,ShowActivated=false,ShowInTaskbar=false};
                window.Show();
                var browser=(WebView2)typeof(MailBodyWindow).GetField("_browser",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
                bool ready=false;
                for(int i=0;i<150;i++)
                {
                    await Task.Delay(100);
                    if(browser.CoreWebView2 != null && await browser.CoreWebView2.ExecuteScriptAsync("document.body && document.body.innerText.includes('본문 테스트')") == "true") {ready=true;break;}
                }
                Check(ready,"실제 WebView2 본문 표시 시간 초과");
                Check(await browser.CoreWebView2!.ExecuteScriptAsync("!!document.querySelector('table') && window.injected === undefined") == "true","원문 표 표시와 스크립트 비실행");
                Check(!browser.CoreWebView2.Settings.IsScriptEnabled && !browser.CoreWebView2.Settings.IsWebMessageEnabled,"메일 전용 웹뷰어 격리 설정");
                Check((await browser.CoreWebView2.CookieManager.GetCookiesAsync("https://erp.teia.co.kr")).Count == 0,"메일 웹뷰어에 ERP 쿠키가 없어야 합니다.");
                using(var file=File.Create(Path.Combine(AppContext.BaseDirectory,"test-data","mail-body-preview.png"))) await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
                Console.WriteLine("PASS 실제 WebView2 메일 본문·표 표시, 스크립트 차단, ERP 쿠키 격리"); result=0;
            }
            catch(Exception error){Console.WriteLine("FAIL mail WebView2: "+error.GetBaseException().Message);}
            finally {window?.Close();app.Shutdown(result);}
        }));
        return app.Run();
    }
}
