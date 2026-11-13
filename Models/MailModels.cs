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
    public string Subject { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string Html { get; init; } = string.Empty;
    public string Plain { get; init; } = string.Empty;
    public int AttachmentCount { get; init; }
}
