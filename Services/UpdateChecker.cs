using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Amaranth10API.Services;

public sealed class AppUpdateInfo
{
    public AppUpdateInfo(
        Version latest,
        string latestLabel,
        Uri installerUri,
        IReadOnlyList<string>? changeNotes = null)
    {
        Latest = latest;
        LatestLabel = latestLabel;
        InstallerUri = installerUri;
        ChangeNotes = changeNotes ?? Array.Empty<string>();
    }

    public Version Latest { get; }

    public string LatestLabel { get; }

    public Uri InstallerUri { get; }

    public IReadOnlyList<string> ChangeNotes { get; }
}

public static class UpdateChecker
{
    public const string Owner = "ermuks";
    public const string Repo = "Amatha";
    public const string Branch = "main";
    public const string InstallerFileName = "아맛다보고서Install.exe";

    private static readonly Uri DistInstallerUri = new(
        $"https://github.com/{Owner}/{Repo}/raw/{Branch}/dist/{Uri.EscapeDataString(InstallerFileName)}");
    private static readonly Uri MediaInstallerUri = new(
        $"https://media.githubusercontent.com/media/{Owner}/{Repo}/{Branch}/dist/{Uri.EscapeDataString(InstallerFileName)}");
    private static readonly Uri LatestReleaseUri = new(
        $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
    private static readonly Uri[] VersionFileUris =
    {
        new($"https://raw.githubusercontent.com/{Owner}/{Repo}/{Branch}/Directory.Build.props"),
        new($"https://github.com/{Owner}/{Repo}/raw/{Branch}/Directory.Build.props"),
        new($"https://api.github.com/repos/{Owner}/{Repo}/contents/Directory.Build.props?ref={Branch}")
    };

    private static readonly HttpClient Http = CreateClient();
    private static readonly Regex VersionElement = new(
        @"<Version>\s*([0-9]+(?:\.[0-9]+){1,3})\s*</Version>",
        RegexOptions.CultureInvariant);

    public static async Task<AppUpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        Version current = Canonical(AppVersion.Current);
        Task<Version?> fileTask = TryReadPublishedVersionAsync(cancellationToken);
        Task<GitHubRelease?> releaseTask = TryReadLatestReleaseAsync(cancellationToken);

        Version? fileVersion = await fileTask.ConfigureAwait(false);
        GitHubRelease? release = await releaseTask.ConfigureAwait(false);
        Version? latest = Max(fileVersion, release?.Version);

        if (latest == null || latest <= current)
        {
            return null;
        }

        Uri installerUri = DistInstallerUri;
        if (release != null && release.Version == latest && release.InstallerUri != null)
        {
            installerUri = release.InstallerUri;
        }

        IReadOnlyList<string> notes = await TryReadChangeNotesAsync(cancellationToken).ConfigureAwait(false);
        return new AppUpdateInfo(latest, FormatLabel(latest), installerUri, notes);
    }

    public static async Task<string> DownloadInstallerAsync(
        AppUpdateInfo update,
        CancellationToken cancellationToken = default)
    {
        string destination = Path.Combine(Path.GetTempPath(), InstallerFileName);
        Uri[] candidates =
        {
            update.InstallerUri,
            DistInstallerUri,
            MediaInstallerUri
        };

        Exception? lastError = null;
        foreach (Uri uri in candidates.Distinct())
        {
            try
            {
                await DownloadFileAsync(uri, destination, cancellationToken);
                if (LooksLikeInstaller(destination))
                {
                    return destination;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
            {
                lastError = exception;
            }
        }

        throw lastError ?? new InvalidOperationException("설치 파일을 받지 못했습니다.");
    }

    public static void LaunchInstaller(string installerPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true,
            Verb = "runas"
        });
    }

    private static async Task<Version?> TryReadPublishedVersionAsync(CancellationToken cancellationToken)
    {
        foreach (Uri uri in VersionFileUris)
        {
            string? text = await TryGetStringAsync(uri, "*/*", cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            Version? parsed = ParsePublishedVersion(text!);
            if (parsed != null)
            {
                return parsed;
            }
        }

        return null;
    }

    private static Version? ParsePublishedVersion(string text)
    {
        if (TryDecodeGithubContents(text, out string decoded))
        {
            text = decoded;
        }

        Match match = VersionElement.Match(text);
        return match.Success && TryParseVersion(match.Groups[1].Value, out Version version)
            ? version
            : null;
    }

    private static bool TryDecodeGithubContents(string text, out string decoded)
    {
        decoded = string.Empty;
        string trimmed = text.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{')
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(trimmed);
            if (!document.RootElement.TryGetProperty("content", out JsonElement content))
            {
                return false;
            }

            string base64 = (content.GetString() ?? string.Empty).Replace("\n", string.Empty);
            if (base64.Length == 0)
            {
                return false;
            }

            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return decoded.Length > 0;
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return false;
        }
    }

    private static async Task<string?> TryGetStringAsync(
        Uri uri,
        string accept,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd(accept);
            using HttpResponseMessage response = await Http.SendAsync(request, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<GitHubRelease?> TryReadLatestReleaseAsync(CancellationToken cancellationToken)
    {
        string? json = await TryGetStringAsync(LatestReleaseUri, "application/vnd.github+json", cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json!);
            JsonElement root = document.RootElement;
            if (!TryParseVersion(root.GetProperty("tag_name").GetString(), out Version version))
            {
                return null;
            }

            Uri? installerUri = null;
            if (root.TryGetProperty("assets", out JsonElement assets))
            {
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out JsonElement nameElement)
                        ? nameElement.GetString() ?? string.Empty
                        : string.Empty;
                    if (!name.EndsWith("Install.exe", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(name, InstallerFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (asset.TryGetProperty("browser_download_url", out JsonElement urlElement) &&
                        Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out Uri? uri))
                    {
                        installerUri = uri;
                        if (string.Equals(name, InstallerFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                    }
                }
            }

            return new GitHubRelease(version, installerUri);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<string>> TryReadChangeNotesAsync(CancellationToken cancellationToken)
    {
        string? sha = AppVersion.SourceRevision;
        if (!string.IsNullOrWhiteSpace(sha) && sha!.Length >= 7)
        {
            Uri compareUri = new(
                $"https://api.github.com/repos/{Owner}/{Repo}/compare/{sha}...{Branch}");
            IReadOnlyList<string> fromCompare = ParseCommitMessages(
                await TryGetStringAsync(compareUri, "application/vnd.github+json", cancellationToken)
                    .ConfigureAwait(false),
                newestFirst: false);
            if (fromCompare.Count > 0)
            {
                return fromCompare;
            }
        }

        Uri commitsUri = new(
            $"https://api.github.com/repos/{Owner}/{Repo}/commits?sha={Branch}&per_page=12");
        return ParseCommitMessages(
            await TryGetStringAsync(commitsUri, "application/vnd.github+json", cancellationToken)
                .ConfigureAwait(false),
            newestFirst: true);
    }

    private static IReadOnlyList<string> ParseCommitMessages(string? json, bool newestFirst)
    {
        List<string> notes = new();
        if (string.IsNullOrWhiteSpace(json))
        {
            return notes;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json!);
            JsonElement root = document.RootElement;
            JsonElement commits = root.ValueKind == JsonValueKind.Array
                ? root
                : root.TryGetProperty("commits", out JsonElement nested)
                    ? nested
                    : default;
            if (commits.ValueKind != JsonValueKind.Array)
            {
                return notes;
            }

            List<string> collected = new();
            foreach (JsonElement item in commits.EnumerateArray())
            {
                if (!item.TryGetProperty("commit", out JsonElement commit) ||
                    !commit.TryGetProperty("message", out JsonElement messageElement))
                {
                    continue;
                }

                string? line = FirstCommitLine(messageElement.GetString());
                if (line == null || collected.Contains(line))
                {
                    continue;
                }

                collected.Add(line);
            }

            if (!newestFirst)
            {
                collected.Reverse();
            }

            foreach (string line in collected)
            {
                notes.Add(line);
                if (notes.Count >= 5)
                {
                    break;
                }
            }
        }
        catch (JsonException)
        {
            return notes;
        }

        return notes;
    }

    private static string? FirstCommitLine(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        string line = message!.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        if (line.Length == 0 ||
            line.StartsWith("Merge ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("병합", StringComparison.Ordinal))
        {
            return null;
        }

        return line.Length <= 72 ? line : line.Substring(0, 71) + "…";
    }

    private static async Task DownloadFileAsync(Uri uri, string destination, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("*/*");
        using HttpResponseMessage response = await Http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        using Stream source = await response.Content.ReadAsStreamAsync();
        using FileStream target = new(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(target, 81920, cancellationToken);
    }

    private static bool LooksLikeInstaller(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        FileInfo info = new(path);
        if (info.Length < 64 * 1024)
        {
            return false;
        }

        byte[] buffer = new byte[80];
        using (FileStream stream = File.OpenRead(path))
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                return false;
            }

            string head = Encoding.ASCII.GetString(buffer, 0, read);
            if (head.StartsWith("version https://git-lfs.github.com/spec/v1", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static HttpClient CreateClient()
    {
        HttpClientHandler handler = new()
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        HttpClient client = new(handler)
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("AmathaBogoso", "1.0"));
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("(update-check)"));
        return client;
    }

    internal static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text!.Trim();
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring(1);
        }

        Match match = Regex.Match(value, @"^\d+\.\d+(?:\.\d+){0,2}");
        if (!match.Success || !Version.TryParse(match.Value, out Version parsed))
        {
            return false;
        }

        version = Canonical(parsed);
        return true;
    }

    private static Version Canonical(Version version)
    {
        return new Version(
            version.Major,
            version.Minor,
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));
    }

    private static Version? Max(Version? left, Version? right)
    {
        if (left == null)
        {
            return right;
        }

        if (right == null)
        {
            return left;
        }

        return left >= right ? left : right;
    }

    private static string FormatLabel(Version version)
    {
        return version.Revision > 0
            ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private sealed class GitHubRelease
    {
        public GitHubRelease(Version version, Uri? installerUri)
        {
            Version = version;
            InstallerUri = installerUri;
        }

        public Version Version { get; }

        public Uri? InstallerUri { get; }
    }
}
