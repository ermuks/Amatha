namespace Amaranth10API.Models;

public sealed class InboxMail
{
    public long Id { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string SenderName { get; init; } = string.Empty;
    public string SenderAddress { get; init; } = string.Empty;
    public string ReceivedAt { get; init; } = string.Empty;
    public bool IsUnread { get; init; }
    public bool HasAttachment { get; init; }
    public string ReadLabel => IsUnread ? "안 읽음" : "읽음";
    public string AttachmentLabel => HasAttachment ? "첨부" : string.Empty;

    /// <summary>목록 표시용 시각: 오늘은 "오후 3:20", 어제는 "어제", 올해는 "11월 7일", 그 밖은 "2025.11.07". 해석할 수 없으면 원문.</summary>
    public string DisplayTime => FormatReceivedAt(ReceivedAt, DateTime.Now);

    public static string FormatReceivedAt(string raw, DateTime now)
    {
        string[] formats = { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy.MM.dd HH:mm:ss", "yyyy.MM.dd HH:mm" };
        if (!DateTime.TryParseExact((raw ?? string.Empty).Trim(), formats, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime value))
        {
            return raw ?? string.Empty;
        }

        if (value.Date == now.Date)
        {
            int hour = value.Hour % 12 == 0 ? 12 : value.Hour % 12;
            return (value.Hour < 12 ? "오전 " : "오후 ") + hour + ":" + value.Minute.ToString("00");
        }

        if (value.Date == now.Date.AddDays(-1))
        {
            return "어제";
        }

        return value.Year == now.Year ? $"{value.Month}월 {value.Day}일" : value.ToString("yyyy.MM.dd");
    }
}

public sealed class InboxMailPage
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int UnreadCount { get; init; }
    public List<InboxMail> Items { get; init; } = new();
    public bool HasNext => (long)Page * PageSize < TotalCount;
}

public sealed class MailBody
{
    public long Id { get; init; }
    public string MailboxEmail { get; init; } = string.Empty;
    public string EmployeeSequence { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string Html { get; init; } = string.Empty;
    public string Plain { get; init; } = string.Empty;
    public List<MailAttachment> Attachments { get; init; } = new();
    public int AttachmentCount => Attachments.Count;
}

public sealed class MailAttachment
{
    public string FileId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string SizeText { get; init; } = string.Empty;
    public long? SizeBytes { get; init; }
    public bool CanDownload => !string.IsNullOrWhiteSpace(FileId);

    /// <summary>확장자 배지 글자 (예: PDF, XLSX). 없으면 FILE</summary>
    public string Extension
    {
        get
        {
            string ext = System.IO.Path.GetExtension(FileName ?? string.Empty).TrimStart('.').ToUpperInvariant();
            return string.IsNullOrEmpty(ext) ? "FILE" : ext.Length > 4 ? ext.Substring(0, 4) : ext;
        }
    }

    /// <summary>확장자 배지 색: pdf · sheet · doc · other</summary>
    public string ExtensionKind => Extension switch
    {
        "PDF" => "pdf",
        "XLS" or "XLSX" or "CSV" => "sheet",
        "HWP" or "HWPX" or "DOC" or "DOCX" => "doc",
        _ => "other"
    };
}

public sealed class MailDownloadProgress
{
    public int Index { get; init; }
    public int Total { get; init; }
    public string FileName { get; init; } = string.Empty;
    public long ReceivedBytes { get; init; }
}

public sealed class MailDownloadResult
{
    public int SavedCount { get; internal set; }
    public bool Cancelled { get; internal set; }
    public List<string> FailedFiles { get; } = new();
}
