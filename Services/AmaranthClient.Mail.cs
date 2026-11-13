using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Amaranth10API.Models;
using Amaranth10API.Helpers;

namespace Amaranth10API.Services;

public sealed partial class AmaranthClient
{
    public async Task<InboxMailPage> GetInboxAsync(AmaranthSession session, int page = 1,
        int pageSize = 100, CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (page < 1 || pageSize < 1 || pageSize > 100) throw new ArgumentOutOfRangeException(nameof(page));
        const string path = "/mail/mail003A01";
        // INBOX는 로그인 계정의 받은메일함을 선택합니다.
        string json = JsonSerializer.Serialize(new { boxName = "INBOX", mainApiCode = "mail003A01",
            page, pageSize, sort = "rfc822date", sortType = "desc", listType = "", showType = "", seen = false });
        return ParseInboxResponse(await SendMailApiAsync(session, path, json, cancellationToken), page, pageSize);
    }

    public async Task<MailBody> GetMailBodyAsync(AmaranthSession session, long uid, CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (uid <= 0) throw new ArgumentOutOfRangeException(nameof(uid));
        string json = JsonSerializer.Serialize(new { uid = uid.ToString(CultureInfo.InvariantCulture) });
        return ParseMailBodyResponse(await SendMailApiAsync(session, "/mail/mail002A01", json, cancellationToken), uid);
    }

    private async Task<string> SendMailApiAsync(AmaranthSession session, string path, string json, CancellationToken cancellationToken)
    {
        using var request = CreateMailRequest(session, path, new StringContent(json, Encoding.UTF8, "application/json"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _httpClient.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"메일을 조회하지 못했습니다. 다시 로그인하거나 잠시 후 새로고침해 주세요. (HTTP {(int)response.StatusCode})");
        string body = await response.Content.ReadAsStringAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return body;
    }

    private static HttpRequestMessage CreateMailRequest(AmaranthSession session, string path, HttpContent content)
    {
        string transactionId = Guid.NewGuid().ToString("N");
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path) { Content = content };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + session.AuthToken);
        request.Headers.TryAddWithoutValidation("access-domain", BaseUrl);
        request.Headers.TryAddWithoutValidation("Origin", BaseUrl);
        request.Headers.TryAddWithoutValidation("Referer", BaseUrl + "/");
        request.Headers.TryAddWithoutValidation("timestamp", timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("transaction-id", transactionId);
        request.Headers.TryAddWithoutValidation("wehago-sign", CreateApiSignature(session.AuthToken, session.HashKey, transactionId, timestamp, path));
        return request;
    }

    public async Task DownloadMailAttachmentAsync(AmaranthSession session, MailBody body, MailAttachment attachment,
        string destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default, bool overwrite = true)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (body == null) throw new ArgumentNullException(nameof(body));
        if (attachment == null) throw new ArgumentNullException(nameof(attachment));
        if (body.Id <= 0 || string.IsNullOrWhiteSpace(body.MailboxEmail) || string.IsNullOrWhiteSpace(body.EmployeeSequence)
            || !attachment.CanDownload || !body.Attachments.Contains(attachment))
            throw new InvalidOperationException("첨부파일 정보를 확인할 수 없습니다. 메일을 다시 불러와 주세요.");
        using var request = CreateMailRequest(session, "/mail/mail002A37", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["fileSn"] = attachment.FileId,
            ["authKeyMap"] = JsonSerializer.Serialize(new { email = body.MailboxEmail,
                muid = body.Id.ToString(CultureInfo.InvariantCulture), empSeq = body.EmployeeSequence }),
            ["moduleGbn"] = "MAIL"
        }));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        using var response = await _attachmentHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"첨부파일을 다운로드하지 못했습니다. (HTTP {(int)response.StatusCode})");
        var disposition = response.Content.Headers.ContentDisposition;
        string type = response.Content.Headers.ContentType?.MediaType ?? "";
        bool isFile = disposition != null && (string.Equals(disposition.DispositionType, "attachment", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(disposition.FileName) || !string.IsNullOrWhiteSpace(disposition.FileNameStar));
        // 로그인 페이지나 API 오류 응답이 사용자의 기존 파일을 덮어쓰지 않도록 합니다.
        if (!isFile && (type.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0
            || type.Equals("text/html", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("파일 대신 오류 응답을 받았습니다. 다시 로그인 후 시도해 주세요.");
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        // .NET Framework의 네트워크 스트림은 ReadAsync 취소만으로 대기가 끝나지 않을 수 있습니다.
        using var registration = timeout.Token.Register(() => stream.Dispose());
        try
        {
            // fileList.fileSize는 MIME 인코딩된 크기일 수 있어 다운로드 길이 검증에 사용하지 않습니다.
            await MailAttachmentFile.SaveAsync(stream, destination, response.Content.Headers.ContentLength,
                progress, timeout.Token, overwrite).ConfigureAwait(false);
        }
        catch (Exception) when (timeout.IsCancellationRequested)
        {
            throw new OperationCanceledException(timeout.Token);
        }
    }

    public async Task<MailDownloadResult> DownloadAllMailAttachmentsAsync(AmaranthSession session, MailBody body,
        string folder, IProgress<MailDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (body == null) throw new ArgumentNullException(nameof(body));
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("저장 폴더를 찾을 수 없습니다.");
        var attachments = body.Attachments.ToArray();
        var result = new MailDownloadResult();
        for (int index = 0; index < attachments.Length; index++)
        {
            if (cancellationToken.IsCancellationRequested) { result.Cancelled = true; break; }
            var attachment = attachments[index];
            int current = index + 1;
            var bytes = new AttachmentByteProgress(received => progress?.Report(new MailDownloadProgress
                { Index = current, Total = attachments.Length, FileName = attachment.FileName, ReceivedBytes = received }));
            bytes.Report(0);
            try
            {
                string destination = MailAttachmentFile.AvailablePath(folder, attachment.FileName);
                // 저장 직전 다른 작업이 같은 경로에 파일을 만들어도 덮어쓰지 않습니다.
                await DownloadMailAttachmentAsync(session, body, attachment, destination, bytes, cancellationToken, overwrite: false).ConfigureAwait(false);
                result.SavedCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result.Cancelled = true;
                break;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException or OperationCanceledException or ArgumentException)
            {
                result.FailedFiles.Add(attachment.FileName);
            }
        }
        return result;
    }

    private sealed class AttachmentByteProgress : IProgress<long>
    {
        private readonly Action<long> _report;
        public AttachmentByteProgress(Action<long> report) { _report = report; }
        public void Report(long value) => _report(value);
    }

    public static MailBody ParseMailBodyResponse(string json, long expectedUid)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (MailNumber(root, "resultCode") != 0) throw new FormatException();
            var data = root.GetProperty("resultData");
            if (MailNumber(data, "code") != 0 || data.GetProperty("muid").GetInt64() != expectedUid) throw new FormatException();
            var mime = data.GetProperty("mime");
            var body = mime.GetProperty("body");
            var decoded = data.GetProperty("decodeMime");
            // decodeMime은 MIME encoded-word를 이미 디코딩한 표시용 헤더입니다.
            return new MailBody {
                Id = expectedUid,
                MailboxEmail = GetString(decoded, "email"),
                EmployeeSequence = GetString(decoded, "empSeq"),
                Subject = WebUtility.HtmlDecode(GetString(decoded, "subject")),
                From = WebUtility.HtmlDecode(GetString(decoded, "from")),
                To = WebUtility.HtmlDecode(GetString(decoded, "to")),
                Date = GetString(decoded, "date"),
                Html = GetString(body, "html"), Plain = GetString(body, "plain"),
                Attachments = ParseMailAttachments(mime)
            };
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or FormatException or InvalidOperationException or OverflowException)
        {
            throw new InvalidOperationException("메일 본문을 읽지 못했습니다. 로그인 상태를 확인하고 다시 시도해 주세요.");
        }
    }

    private static List<MailAttachment> ParseMailAttachments(JsonElement mime)
    {
        var result = new List<MailAttachment>();
        if (!mime.TryGetProperty("fileList", out var files) || files.ValueKind == JsonValueKind.Null) return result;
        foreach (var file in files.EnumerateArray())
        {
            string name = WebUtility.HtmlDecode(GetString(file, "originalFileName"));
            string size = GetString(file, "fileSize");
            long? sizeBytes = null;
            if (long.TryParse(size, NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytes) && bytes >= 0)
            {
                sizeBytes = bytes;
                size = bytes >= 1048576 ? $"{bytes / 1048576.0:0.#} MB" : bytes >= 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes} B";
            }
            result.Add(new MailAttachment { FileId = GetString(file, "fileSn"),
                FileName = string.IsNullOrWhiteSpace(name) ? "이름 없는 첨부파일" : name, SizeText = size, SizeBytes = sizeBytes });
        }
        return result;
    }

    public static InboxMailPage ParseInboxResponse(string json, int page, int pageSize)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (MailNumber(root, "resultCode") != 0)
                throw new InvalidOperationException("메일 조회 권한이나 로그인 상태를 확인해 주세요.");
            JsonElement data = root.GetProperty("resultData");
            if (MailNumber(data, "code") != 0 || GetString(data, "Result") != "OK")
                throw new InvalidOperationException("받은메일함을 조회하지 못했습니다. 다시 로그인 후 시도해 주세요.");
            int total = MailNumber(data, "TotalRecordCount");
            int unread = MailNumber(data, "TotalUnseenCount");
            if (total < 0 || unread < 0) throw new FormatException();
            var items = new List<InboxMail>();
            foreach (JsonElement row in data.GetProperty("Records").EnumerateArray())
            {
                string sender = WebUtility.HtmlDecode(GetString(row, "fromAddrName"));
                string address = GetString(row, "fromAddrEmail");
                string subject = WebUtility.HtmlDecode(GetString(row, "subject"));
                string date = GetString(row, "tooltipDate");
                items.Add(new InboxMail {
                    Id = row.GetProperty("muid").GetInt64(),
                    Subject = string.IsNullOrWhiteSpace(subject) ? "(제목 없음)" : subject,
                    SenderName = string.IsNullOrWhiteSpace(sender) ? address : sender,
                    SenderAddress = address,
                    ReceivedAt = string.IsNullOrWhiteSpace(date) ? GetString(row, "rfc822date") : date,
                    IsUnread = MailNumber(row, "seen") == 0,
                    HasAttachment = row.TryGetProperty("attach", out var attach) && attach.ValueKind == JsonValueKind.True
                });
            }
            return new InboxMailPage { Page = page, PageSize = pageSize, TotalCount = total, UnreadCount = unread, Items = items };
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or FormatException or InvalidOperationException or OverflowException)
        {
            // 원문 응답에는 메일과 인증 관련 정보가 포함될 수 있어 화면·로그에 노출하지 않습니다.
            throw new InvalidOperationException("메일 목록을 읽지 못했습니다. 로그인 상태를 확인하고 다시 시도해 주세요.");
        }
    }

    private static int MailNumber(JsonElement element, string name)
    {
        JsonElement value = element.GetProperty(name);
        return value.ValueKind == JsonValueKind.Number ? value.GetInt32() : int.Parse(value.GetString()!, CultureInfo.InvariantCulture);
    }
}
