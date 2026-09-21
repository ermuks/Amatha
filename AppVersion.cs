using System.Reflection;
using System.Text.RegularExpressions;

internal static class AppVersion
{
    public static string Number { get; } = Read();

    public static Version Current
    {
        get
        {
            if (!Version.TryParse(Number, out Version parsed))
            {
                return new Version(0, 0, 0, 0);
            }

            return new Version(
                parsed.Major,
                parsed.Minor,
                Math.Max(parsed.Build, 0),
                Math.Max(parsed.Revision, 0));
        }
    }

    public static string FooterLabel => "ver " + Number;

    public static string InstallButtonLabel => "아맛다보고서 " + Number + " 설치";

    public static string? SourceRevision
    {
        get
        {
            string informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?? string.Empty;
            int plus = informational.IndexOf('+');
            if (plus < 0)
            {
                return null;
            }

            Match match = Regex.Match(informational.Substring(plus + 1), "[0-9a-fA-F]{7,40}");
            return match.Success ? match.Value : null;
        }
    }

    private static string Read()
    {
        string informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? string.Empty;

        if (informational.Length > 0)
        {
            int plus = informational.IndexOf('+');
            string value = plus >= 0 ? informational.Substring(0, plus) : informational;
            Match match = Regex.Match(value, @"^\d+\.\d+(?:\.\d+){0,2}");
            if (match.Success)
            {
                return match.Value;
            }
        }

        Version? version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
