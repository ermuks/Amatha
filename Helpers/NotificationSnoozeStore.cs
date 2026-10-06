using System.IO;
using System.Text.Json;

namespace Amaranth10API.Helpers;

public sealed class NotificationSnoozeStore
{
    public static readonly TimeSpan Duration = TimeSpan.FromHours(2);

    private readonly string? _filePath;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Dictionary<string, DateTimeOffset> _expiresAt = new(StringComparer.Ordinal);

    public NotificationSnoozeStore(string? filePath = null, Func<DateTimeOffset>? utcNow = null)
    {
        _filePath = filePath;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        Load();
    }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "아맛다보고서",
        "notification-snoozes.json");

    public bool IsSnoozed(string key)
    {
        return _expiresAt.TryGetValue(key, out DateTimeOffset expiresAt) && expiresAt > _utcNow();
    }

    public void SetSnoozed(string key, bool snoozed)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("알림 항목의 식별자가 필요합니다.", nameof(key));
        }

        RemoveExpired();
        if (snoozed)
        {
            _expiresAt[key] = _utcNow().Add(Duration);
        }
        else
        {
            _expiresAt.Remove(key);
        }

        Save();
    }

    private void RemoveExpired()
    {
        DateTimeOffset now = _utcNow();
        foreach (string key in _expiresAt.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
        {
            _expiresAt.Remove(key);
        }
    }

    private void Load()
    {
        if (_filePath == null)
        {
            return;
        }

        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            Dictionary<string, DateTimeOffset>? saved =
                JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(_filePath));
            if (saved != null)
            {
                foreach (KeyValuePair<string, DateTimeOffset> pair in saved)
                {
                    _expiresAt[pair.Key] = pair.Value;
                }
            }

            RemoveExpired();
        }
        catch
        {
            // 저장 파일을 읽지 못해도 알림과 체크박스는 메모리 상태로 동작합니다.
        }
    }

    private void Save()
    {
        if (_filePath == null)
        {
            return;
        }

        try
        {
            string? folder = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(_filePath, JsonSerializer.Serialize(_expiresAt));
        }
        catch
        {
            // 저장 실패는 현재 실행 중인 앱의 알림 중지를 방해하지 않습니다.
        }
    }
}
