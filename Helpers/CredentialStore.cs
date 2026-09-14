using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Amaranth10API.Helpers;

public static class CredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Teia.AmattahReport.Login");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string FilePath
    {
        get
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "아맛다보고서");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "login.bin");
        }
    }

    public static void Save(string loginId, string password)
    {
        SavedLogin saved = new()
        {
            LoginId = loginId,
            Password = password
        };
        byte[] plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(saved, JsonOptions));
        byte[] protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, protectedBytes);
    }

    public static SavedLogin? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            byte[] protectedBytes = File.ReadAllBytes(FilePath);
            if (protectedBytes.Length == 0)
            {
                return null;
            }

            byte[] plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            SavedLogin? saved = JsonSerializer.Deserialize<SavedLogin>(Encoding.UTF8.GetString(plain), JsonOptions);
            if (saved == null ||
                string.IsNullOrWhiteSpace(saved.LoginId) ||
                string.IsNullOrWhiteSpace(saved.Password))
            {
                return null;
            }

            return saved;
        }
        catch
        {
            return null;
        }
    }

    public static void Delete()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch
        {
            // 로그인 정보 삭제 실패는 프로그램 동작을 막지 않습니다.
        }
    }
}

public sealed class SavedLogin
{
    public string LoginId { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
