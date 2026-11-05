using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Amaranth10API.Helpers;

namespace Amaranth10Installer;

public static class Installation
{
    public static void InstallTo(string destination, Action<int> report)
    {
        CloseRunningApp(destination);
        Directory.CreateDirectory(destination);
        report(4);

        using Stream payload = OpenPayload();
        using ZipArchive zip = new(payload, ZipArchiveMode.Read);
        IReadOnlyList<ZipArchiveEntry> entries = zip.Entries;
        int total = Math.Max(1, entries.Count);
        string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        for (int i = 0; i < entries.Count; i++)
        {
            ZipArchiveEntry entry = entries[i];
            string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(Path.Combine(root, relative));
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("설치 패키지 경로가 올바르지 않습니다.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(fullPath);
            }
            else
            {
                string? folder = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                entry.ExtractToFile(fullPath, overwrite: true);
            }

            int percent = 4 + (int)Math.Round((i + 1) * 96.0 / total);
            report(percent);
        }
    }

    private static Stream OpenPayload()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        Stream? stream = assembly.GetManifestResourceStream("payload.zip");
        if (stream != null)
        {
            return stream;
        }

        string? name = assembly.GetManifestResourceNames()
            .FirstOrDefault(item => item.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));
        if (name != null)
        {
            stream = assembly.GetManifestResourceStream(name);
            if (stream != null)
            {
                return stream;
            }
        }

        throw new InvalidOperationException("설치 패키지를 찾지 못했습니다.");
    }

    private static void CloseRunningApp(string destination)
    {
        string target = Path.GetFullPath(Path.Combine(destination, SilentUpdateOptions.ExeName));
        foreach (Process process in Process.GetProcessesByName("아맛다보고서"))
        {
            using (process)
            {
                string? runningPath;
                try { runningPath = process.MainModule?.FileName; }
                catch (InvalidOperationException) { continue; } // 이미 종료된 프로세스
                catch (System.ComponentModel.Win32Exception) { continue; } // 다른 사용자 프로세스
                if (!string.Equals(runningPath, target, StringComparison.OrdinalIgnoreCase)) continue;
                process.CloseMainWindow();
                if (!process.WaitForExit(3000))
                {
                    process.Kill();
                    if (!process.WaitForExit(5000)) throw new IOException("실행 중인 프로그램을 종료하지 못했습니다.");
                }
            }
        }
    }

    // 기존 Explorer를 통해 실행하여 관리자 설치기의 권한을 앱에 상속하지 않습니다.
    public static void LaunchInstalledApp(string exePath, bool tray)
    {
        string target = tray ? CreateTrayShortcut(exePath) : exePath;
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            Arguments = "\"" + target + "\"",
            UseShellExecute = true
        });
    }

    public static string CreateTrayShortcut(string exePath)
    {
        string fullPath = Path.GetFullPath(exePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("실행 파일을 찾지 못했습니다.", fullPath);
        string linkPath = Path.Combine(Path.GetDirectoryName(fullPath)!, ".아맛다보고서-tray.lnk");
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            dynamic dynamicShell = shell!;
            shortcut = dynamicShell.CreateShortcut(linkPath);
            dynamic link = shortcut;
            link.TargetPath = fullPath;
            link.Arguments = "--tray";
            link.WorkingDirectory = Path.GetDirectoryName(fullPath);
            link.Save();
            return linkPath;
        }
        finally
        {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
    }
}
