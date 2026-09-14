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
    public AppUpdateInfo(Version latest, string latestLabel, Uri installerUri)
    {
        Latest = latest;
        LatestLabel = latestLabel;
        InstallerUri = installerUri;
    }

    public Version Latest { get; }

    public string LatestLabel { get; }

    public Uri InstallerUri { get; }
}

public static class UpdateChecker
{
    public const string Owner = "ermuks";
    public const string Repo = "Amatha";
    public const string Branch = "main";
    public const string InstallerFileName = "아맛다보고서Install.exe";

    private static readonly Uri VersionFileUri = new(
        $"https://raw.githubusercontent.com/{Owner}/{Repo}/{Branch}/Directory.Build.props");
    private static readonly Uri DistInstallerUri = new(
        $"https://github.com/{Owner}/{Repo}/raw/{Branch}/dist/{Uri.EscapeDataString(InstallerFileName)}");
    private static readonly Uri MediaInstallerUri = new(
        $"https://media.githubusercontent.com/media/{Owner}/{Repo}/{Branch}/dist/{Uri.EscapeDataString(InstallerFileName)}");
    private static readonly Uri LatestReleaseUri = new(
        $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

    private static readonly HttpClient Http = CreateClient();
    private static readonly Regex VersionElement = new(
        @"<Version>\s*([0-9]+(?:\.[0-9]+){1,3})\s*</Version>",
        RegexOptions.CultureInvariant);

    public static async Task<AppUpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));

        Version current = Canonical(AppVersion.Current);
        GitHubRelease? release = await TryReadLatestReleaseAsync(timeout.Token);
        Version? fileVersion = await TryReadPublishedVersionAsync(timeout.Token);
        Version? latest = Max(release?.Version, fileVersion);

        if (latest == null || latest <= current)
        {
            return null;
        }

        Uri installerUri = DistInstallerUri;
        if (release != null && release.Version == latest && release.InstallerUri != null)
        {
            installerUri = release.InstallerUri;
        }

        return new AppUpdateInfo(latest, FormatLabel(latest), installerUri);
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
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, VersionFileUri);
            request.Headers.Accept.ParseAdd("text/plain");
            using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string text = await response.Content.ReadAsStringAsync();
            Match match = VersionElement.Match(text);
            return match.Success && TryParseVersion(match.Groups[1].Value, out Version version)
                ? version
                : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static async Task<GitHubRelease?> TryReadLatestReleaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, LatestReleaseUri);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
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
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
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
            new ProductInfoHeaderValue("AmathaBogoso", AppVersion.Number));
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
