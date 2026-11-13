using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Amaranth10API.Models;

namespace Amaranth10API.Services;

public sealed partial class AmaranthClient
{
    public async Task<InboxMailPage> GetInboxAsync(AmaranthSession session, int page = 1,
        int pageSize = 100, CancellationToken cancellationToken = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (page < 1 || pageSize < 1 || pageSize > 100) throw new ArgumentOutOfRangeException(nameof(page));
        const string path = "/mail/mail003A01";
        string transactionId = Guid.NewGuid().ToString("N");
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // INBOX는 로그인 계정의 받은메일함을 선택합니다. 다른 계정의 mboxSeq를 재사용하지 않습니다.
        string json = JsonSerializer.Serialize(new { boxName = "INBOX", mainApiCode = "mail003A01",
            page, pageSize, sort = "rfc822date", sortType = "desc", listType = "", showType = "", seen = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + session.AuthToken);
        request.Headers.TryAddWithoutValidation("access-domain", BaseUrl);
        request.Headers.TryAddWithoutValidation("Origin", BaseUrl);
        request.Headers.TryAddWithoutValidation("Referer", BaseUrl + "/");
        request.Headers.TryAddWithoutValidation("timestamp", timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("transaction-id", transactionId);
        request.Headers.TryAddWithoutValidation("wehago-sign", CreateApiSignature(session.AuthToken, session.HashKey, transactionId, timestamp, path));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _httpClient.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"메일을 조회하지 못했습니다. 다시 로그인하거나 잠시 후 새로고침해 주세요. (HTTP {(int)response.StatusCode})");
        string body = await response.Content.ReadAsStringAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return ParseInboxResponse(body, page, pageSize);
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
