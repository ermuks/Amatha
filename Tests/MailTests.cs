using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using Amaranth10API.Services;
using Amaranth10API.Models;
using Amaranth10API.Views;

namespace Amaranth10API.Tests;
internal static class MailTests
{
    private const string Fixture = """
        {"resultCode":0,"resultData":{"code":"0","Result":"OK","TotalRecordCount":201,"TotalUnseenCount":1,"Records":[
        {"muid":123,"subject":"프로젝트 &amp; 일정 확인","fromAddrName":"담당자","fromAddrEmail":"sender@example.com","tooltipDate":"2026-11-13 06:56:58","seen":0,"attach":true},
        {"muid":124,"subject":"","fromAddrName":"","fromAddrEmail":"other@example.com","rfc822date":"11-12 18:32","seen":1,"attach":false}]}}
        """;
    private static void Check(bool okay, string message) { if (!okay) throw new Exception(message); }
    public static void Verify()
    {
        var page = AmaranthClient.ParseInboxResponse(Fixture, 1, 100);
        Check(page.TotalCount == 201 && page.UnreadCount == 1 && page.HasNext && page.Items.Count == 2, "페이지 집계 오류");
        Check(page.Items[0].Subject == "프로젝트 & 일정 확인" && page.Items[0].IsUnread && page.Items[0].HasAttachment, "제목·읽음·첨부 파싱 오류");
        Check(page.Items[1].Subject == "(제목 없음)" && page.Items[1].SenderName == "other@example.com" && !page.Items[1].IsUnread, "기본값 처리 오류");
        Check(!AmaranthClient.ParseInboxResponse(Fixture, 3, 100).HasNext, "마지막 페이지 판정 오류");
        string empty = "{\"resultCode\":0,\"resultData\":{\"code\":\"0\",\"Result\":\"OK\",\"TotalRecordCount\":0,\"TotalUnseenCount\":0,\"Records\":[]}}";
        Check(AmaranthClient.ParseInboxResponse(empty, 1, 100).Items.Count == 0, "빈 목록 처리 오류");
        foreach (string bad in new[] { "{}", "not json", Fixture.Replace("\"resultCode\":0", "\"resultCode\":401"), Fixture.Replace("\"code\":\"0\"", "\"code\":\"401\""), Fixture.Replace("\"Records\":", "\"Missing\":") })
        {
            bool rejected = false;
            try { AmaranthClient.ParseInboxResponse(bad, 1, 100); } catch (InvalidOperationException e) { rejected = !e.Message.Contains("sender@example.com"); }
            Check(rejected, "오류 응답을 빈 받은메일함으로 처리하면 안 됨");
        }
        var handler = new Handler();
        var client = new AmaranthClient();
        typeof(AmaranthClient).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, new HttpClient(handler));
        var session = new AmaranthSession { AuthToken = "test-token", HashKey = "test-key" };
        var result = client.GetInboxAsync(session, 2).GetAwaiter().GetResult();
        Check(result.Page == 2 && handler.Valid, "페이지와 서명 요청 검증 실패");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool stopped = false;
        try { client.GetInboxAsync(session, cancellationToken: cancelled.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { stopped = true; }
        Check(stopped, "조회 취소 처리 오류");
    }
    private sealed class Handler : HttpMessageHandler
    {
        public bool Valid;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string body = await request.Content!.ReadAsStringAsync();
            string id = request.Headers.GetValues("transaction-id").Single();
            string timestamp = request.Headers.GetValues("timestamp").Single();
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("test-key"));
            string signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes("test-token" + id + timestamp + "/mail/mail003A01")));
            Valid = request.Headers.GetValues("wehago-sign").Single() == signature && request.Headers.GetValues("access-domain").Single() == "https://erp.teia.co.kr"
                && body.Contains("\"page\":2") && body.Contains("\"boxName\":\"INBOX\"") && !body.Contains("mboxSeq") && body.Contains("\"seen\":false");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixture) };
        }
    }
    public static void Layout()
    {
        var window = new MailWindow(new AmaranthClient(), new AmaranthSession());
        var grid = (DataGrid)window.FindName("MailGrid");
        grid.ItemsSource = AmaranthClient.ParseInboxResponse(Fixture, 1, 100).Items;
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1000, 640)); content.Arrange(new Rect(0, 0, 1000, 640)); content.UpdateLayout();
        Check(grid.Items.Count == 2 && grid.Columns.Count == 5 && grid.IsReadOnly, "메일 목록 XAML 렌더링 오류");
        var bitmap = new RenderTargetBitmap(1000, 640, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, "test-data", "mail-preview.png"))) encoder.Save(file);
        window.Close();
    }
}
