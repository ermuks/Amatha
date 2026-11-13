using System.IO;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Amaranth10API.Helpers;
using Amaranth10API.Models;
using Amaranth10API.Services;

namespace Amaranth10API.Tests;
internal static class MailAttachmentTests
{
    public static void Verify()
    {
        string folder = Path.Combine(AppContext.BaseDirectory,"test-data","attachments-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder,"저장 파일.txt");
        byte[] bytes = Encoding.UTF8.GetBytes("첨부파일 저장 검증");
        MailAttachmentFile.SaveAsync(new MemoryStream(bytes), path, bytes.Length).GetAwaiter().GetResult();
        if (!File.ReadAllBytes(path).SequenceEqual(bytes)) throw new Exception("새 파일 저장 오류");
        bool failed=false;
        try { MailAttachmentFile.SaveAsync(new MemoryStream(new byte[1]), path, 10).GetAwaiter().GetResult(); } catch(IOException) { failed=true; }
        if(!failed || !File.ReadAllBytes(path).SequenceEqual(bytes)) throw new Exception("다운로드 실패 시 기존 파일 유지 오류");
        using var cancelled = new CancellationTokenSource();cancelled.Cancel();
        try { MailAttachmentFile.SaveAsync(new MemoryStream(bytes),path,bytes.Length,cancellationToken:cancelled.Token).GetAwaiter().GetResult(); } catch(OperationCanceledException) { }
        if(Directory.GetFiles(folder,"*.tmp").Length != 0 || !File.ReadAllBytes(path).SequenceEqual(bytes)) throw new Exception("취소 후 임시 파일 정리 오류");
        if(MailAttachmentFile.SuggestedName("../../도면.pdf") != "도면.pdf" || MailAttachmentFile.SuggestedName("CON.txt") != "_CON.txt") throw new Exception("파일명 정리 오류");
        if (MailAttachmentFile.SuggestedName("도면:최종?.pdf") != "도면_최종_.pdf" || MailAttachmentFile.SuggestedName("CON.part.txt") != "_CON.part.txt"
            || MailAttachmentFile.SuggestedName(new string('a', 250) + ".pdf").Length > 180) throw new Exception("특수 문자·예약 이름·긴 파일명 처리 오류");
        byte[] replacement = Encoding.UTF8.GetBytes("성공한 덮어쓰기");
        MailAttachmentFile.SaveAsync(new MemoryStream(replacement), path, replacement.Length).GetAwaiter().GetResult();
        if (!File.ReadAllBytes(path).SequenceEqual(replacement)) throw new Exception("완료 후 덮어쓰기 오류");
        VerifyTransport(folder);
    }

    private static void VerifyTransport(string folder)
    {
        var attachment = new MailAttachment { FileId = "opaque+/=한글", FileName = "파일.txt", SizeBytes = 4 };
        var body = new MailBody { Id = 42, MailboxEmail = "test+mail@example.com", EmployeeSequence = "77", Attachments = new() { attachment } };
        var session = new AmaranthSession { AuthToken = "test-token", HashKey = "test-key" };
        var client = new AmaranthClient();
        var handler = new DownloadHandler();
        using var http = new HttpClient(handler);
        typeof(AmaranthClient).GetField("_attachmentHttpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, http);
        string path = Path.Combine(folder, "transport.txt");
        client.DownloadMailAttachmentAsync(session, body, attachment, path).GetAwaiter().GetResult();
        if (!File.ReadAllBytes(path).SequenceEqual(new byte[] { 1, 2, 3, 4 })) throw new Exception("파일 응답 저장 오류");
        foreach (string mode in new[] { "http", "redirect", "json", "html", "short", "cancel", "stream-cancel" })
        {
            handler.Mode = mode;
            using var cancellation = new CancellationTokenSource();
            if (mode == "cancel") cancellation.Cancel();
            if (mode == "stream-cancel") cancellation.CancelAfter(100);
            bool rejected = false;
            try
            {
                var task = client.DownloadMailAttachmentAsync(session, body, attachment, path, cancellationToken: cancellation.Token);
                if (Task.WhenAny(task, Task.Delay(3000)).GetAwaiter().GetResult() != task) throw new Exception("취소 후 다운로드 대기가 끝나지 않음");
                task.GetAwaiter().GetResult();
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or OperationCanceledException) { rejected = true; }
            if (!rejected || !File.ReadAllBytes(path).SequenceEqual(new byte[] { 1, 2, 3, 4 })) throw new Exception(mode + " 오류 시 기존 파일 손상");
        }
        handler.Mode = "chunked";
        client.DownloadMailAttachmentAsync(session, body, attachment, path).GetAwaiter().GetResult();
        if (!File.ReadAllBytes(path).SequenceEqual(new byte[] { 8, 9 })) throw new Exception("목록의 MIME 크기와 실제 파일 크기가 다른 응답 처리");
        handler.Mode = "json-file";
        client.DownloadMailAttachmentAsync(session, body, attachment, path).GetAwaiter().GetResult();
        if (File.ReadAllText(path) != "{}") throw new Exception("정상 JSON 첨부 거부");
        if (Directory.GetFiles(folder, "*.tmp").Length != 0) throw new Exception("임시 파일 잔존");
    }

    public static void VerifyBatch()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "test-data", "batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string original = Path.Combine(folder, "도면.pdf");
        File.WriteAllText(original, "existing");
        Directory.CreateDirectory(Path.Combine(folder, "도면 (1).pdf"));
        var body = new MailBody { Id = 42, MailboxEmail = "test@example.com", EmployeeSequence = "77", Attachments = new() {
            new MailAttachment { FileId = "first", FileName = "../도면.pdf" },
            new MailAttachment { FileId = "second", FileName = "도면.pdf" },
            new MailAttachment { FileId = "third", FileName = "마지막.txt" }
        } };
        var client = new AmaranthClient();
        var handler = new BatchHandler();
        using var http = new HttpClient(handler);
        typeof(AmaranthClient).GetField("_attachmentHttpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, http);
        var session = new AmaranthSession { AuthToken = "fixture", HashKey = "fixture" };
        var result = client.DownloadAllMailAttachmentsAsync(session, body, folder).GetAwaiter().GetResult();
        if (result.SavedCount != 3 || result.Cancelled || result.FailedFiles.Count != 0 || handler.Calls != 3
            || File.ReadAllText(original) != "existing" || File.ReadAllText(Path.Combine(folder,"도면 (2).pdf")) != "first"
            || File.ReadAllText(Path.Combine(folder,"도면 (3).pdf")) != "second") throw new Exception("전체 저장·동명 파일 및 폴더 충돌 처리");
        bool protectedFile = false;
        try { MailAttachmentFile.SaveAsync(new MemoryStream(new byte[1]), original, 1, overwrite: false).GetAwaiter().GetResult(); }
        catch (IOException) { protectedFile = true; }
        if (!protectedFile || File.ReadAllText(original) != "existing") throw new Exception("저장 직전 경합 시 덮어쓰기 방지");
        handler.Calls = 0; handler.FailSecond = true;
        result = client.DownloadAllMailAttachmentsAsync(session, body, folder).GetAwaiter().GetResult();
        if (result.SavedCount != 2 || result.FailedFiles.Count != 1 || result.FailedFiles[0] != "도면.pdf" || handler.Calls != 3)
            throw new Exception("일부 실패 후 다음 첨부파일 다운로드");
        handler.Calls = 0; handler.FailSecond = false;
        using var cancellation = new CancellationTokenSource(); handler.CancelSecond = cancellation;
        result = client.DownloadAllMailAttachmentsAsync(session, body, folder, cancellationToken: cancellation.Token).GetAwaiter().GetResult();
        if (!result.Cancelled || result.SavedCount != 1 || result.FailedFiles.Count != 0 || handler.Calls != 2)
            throw new Exception("전체 다운로드 취소 후 다음 요청 중단과 완료 수 유지");
        handler.Calls = 0;
        result = client.DownloadAllMailAttachmentsAsync(session, body, folder, cancellationToken: cancellation.Token).GetAwaiter().GetResult();
        if (!result.Cancelled || result.SavedCount != 0 || handler.Calls != 0) throw new Exception("시작 전 취소");
        result = client.DownloadAllMailAttachmentsAsync(session, new MailBody(), folder).GetAwaiter().GetResult();
        if (result.SavedCount != 0 || handler.Calls != 0 || Directory.GetFiles(folder,"*.tmp").Length != 0)
            throw new Exception("빈 첨부 목록 또는 임시 파일 정리");
    }

    private sealed class BatchHandler : HttpMessageHandler
    {
        public int Calls;
        public bool FailSecond;
        public CancellationTokenSource? CancelSecond;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            if (Calls == 2) CancelSecond?.Cancel();
            token.ThrowIfCancellationRequested();
            string form = await request.Content!.ReadAsStringAsync();
            string fileId = WebUtility.UrlDecode(form.Split('&').Single(x => x.StartsWith("fileSn=")).Substring(7));
            return new HttpResponseMessage(FailSecond && Calls == 2 ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
                { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(fileId)) };
        }
    }

    private sealed class DownloadHandler : HttpMessageHandler
    {
        public string Mode = "ok";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string form = await request.Content!.ReadAsStringAsync();
            var values = form.Split('&').Select(x => x.Split(new[] { '=' }, 2)).ToDictionary(x => WebUtility.UrlDecode(x[0]), x => WebUtility.UrlDecode(x[1]));
            using var auth = JsonDocument.Parse(values["authKeyMap"]);
            string id = request.Headers.GetValues("transaction-id").Single();
            string timestamp = request.Headers.GetValues("timestamp").Single();
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("test-key"));
            string signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes("test-token" + id + timestamp + "/mail/mail002A37")));
            if (request.Method != HttpMethod.Post || request.RequestUri!.AbsolutePath != "/mail/mail002A37"
                || request.Content.Headers.ContentType!.MediaType != "application/x-www-form-urlencoded"
                || values.Count != 3 || values["fileSn"] != "opaque+/=한글" || values["moduleGbn"] != "MAIL"
                || auth.RootElement.GetProperty("email").GetString() != "test+mail@example.com"
                || auth.RootElement.GetProperty("muid").GetString() != "42" || auth.RootElement.GetProperty("empSeq").GetString() != "77"
                || request.Headers.GetValues("wehago-sign").Single() != signature || request.Headers.Authorization!.Parameter != "test-token")
                throw new Exception("다운로드 폼·서명·계정 매핑 오류");
            var response = new HttpResponseMessage(Mode == "http" ? HttpStatusCode.Forbidden : Mode == "redirect" ? HttpStatusCode.Redirect : HttpStatusCode.OK);
            response.Content = Mode == "json" || Mode == "json-file" ? new StringContent("{}", Encoding.UTF8, "application/json")
                : Mode == "html" ? new StringContent("<html>Login</html>", Encoding.UTF8, "text/html")
                : Mode == "chunked" || Mode == "stream-cancel" ? new StreamContent(new NetworkStreamFixture(Mode == "stream-cancel"))
                : new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
            if (Mode == "short") response.Content.Headers.ContentLength = 100;
            if (Mode == "json-file") response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "test.json" };
            return response;
        }
    }

    private sealed class NetworkStreamFixture : Stream
    {
        private readonly bool _stall;
        private readonly MemoryStream _data = new(new byte[] { 8, 9 });
        private readonly TaskCompletionSource<int> _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public NetworkStreamFixture(bool stall) { _stall = stall; }
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            _stall ? _pending.Task : _data.ReadAsync(buffer, offset, count, token);
        protected override void Dispose(bool disposing)
        {
            _pending.TrySetException(new ObjectDisposedException(nameof(NetworkStreamFixture)));
            if (disposing) _data.Dispose();
            base.Dispose(disposing);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
