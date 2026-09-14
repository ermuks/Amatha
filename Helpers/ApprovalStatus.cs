namespace Amaranth10API.Helpers;

internal static class ApprovalStatus
{
    public static bool IsRejected(string? status, string? code = null)
    {
        return EqualsToken(status, "반려") || EqualsToken(code, "100");
    }

    public static bool IsInProgress(string? status, string? code = null)
    {
        return EqualsToken(status, "진행") || EqualsToken(code, "30");
    }

    public static bool IsClosed(string? status, string? code = null)
    {
        return EqualsToken(status, "종결") || EqualsToken(code, "90");
    }

    private static bool EqualsToken(string? value, string token)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return string.Equals(value.Trim(), token, StringComparison.Ordinal);
    }
}
