using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Amaranth10API.Helpers;

public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "아맛다보고서";

    public static void Apply(bool enabled)
    {
        if (enabled)
        {
            Register();
            return;
        }

        Unregister();
    }

    public static void Register()
    {
        string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return;
        }

        string shortcutPath = GetStartupShortcutPath();
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        ShellLink.Create(shortcutPath, Path.GetFullPath(exePath), WindowsNotification.AppId, "--autostart");
        RemoveRunKey();
    }

    public static void Unregister()
    {
        string shortcutPath = GetStartupShortcutPath();
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }

        RemoveRunKey();
    }

    private static string GetStartupShortcutPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "아맛다보고서.lnk");
    }

    private static void RemoveRunKey()
    {
        using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey, true);
        run?.DeleteValue(ValueName, false);

        using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, true);
        approved?.DeleteValue(ValueName, false);
    }
}
