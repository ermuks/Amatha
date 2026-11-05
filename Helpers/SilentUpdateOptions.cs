using System.IO;

namespace Amaranth10API.Helpers;

public static class SilentUpdateOptions
{
    public const string ExeName = "아맛다보고서.exe";

    public static string CreateArguments(string executablePath) => "--silent \"" + ValidatePath(executablePath) + "\"";

    public static string? Parse(string[] args)
    {
        if (args.Length == 0) return null;
        if (args.Length != 2 || !string.Equals(args[0], "--silent", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("업데이트 인자는 --silent와 현재 프로그램의 전체 실행 파일 경로여야 합니다.");
        string path = ValidatePath(args[1]);
        if (!File.Exists(path)) throw new FileNotFoundException("업데이트할 프로그램을 찾지 못했습니다.", path);
        return path;
    }

    private static string ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('"') >= 0 || !Path.IsPathRooted(path) ||
            !string.Equals(Path.GetFileName(path), ExeName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("현재 프로그램의 전체 실행 파일 경로가 필요합니다.");
        string fullPath = Path.GetFullPath(path);
        if (!string.Equals(fullPath, path, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("정규화된 전체 실행 파일 경로가 필요합니다.");
        return fullPath;
    }
}
