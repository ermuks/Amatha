using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Amaranth10API.Helpers;

public static class WindowsNotification
{
    public const string AppId = "Teia.AmattahReport";

    public static void Initialize()
    {
        NativeMethods.SetCurrentProcessExplicitAppUserModelID(AppId);
        try
        {
            TryCreateStartMenuShortcut();
        }
        catch
        {
            // 바로가기가 없어도 알림 자체는 시도합니다.
        }
    }

    public static Action<string, string>? TrayBalloon { get; set; }

    public static void ShowMissingReports(int tripCount, int holidayCount, bool showGui = false)
    {
        if (tripCount <= 0 && holidayCount <= 0)
        {
            return;
        }

        string title = "작성하지 않은 보고서가 있습니다";
        string body = FormatBody(tripCount, holidayCount);

        void Show()
        {
            if (TrayBalloon != null)
            {
                TrayBalloon.Invoke(title, body);
            }
            else
            {
                TryShowToast(title, body);
            }

            if (showGui)
            {
                ActivateMainWindow();
            }
        }

        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            Show();
            return;
        }

        dispatcher.BeginInvoke(new Action(Show));
    }

    private static bool TryShowToast(string title, string body)
    {
        try
        {
            string xml =
                "<toast>" +
                "<visual><binding template=\"ToastGeneric\">" +
                "<text>" + EscapeXml(title) + "</text>" +
                "<text>" + EscapeXml(body) + "</text>" +
                "</binding></visual>" +
                "</toast>";

            XmlDocument document = new();
            document.LoadXml(xml);

            ToastNotifier notifier = ToastNotificationManager.CreateToastNotifier(AppId);
            if (notifier.Setting != NotificationSetting.Enabled)
            {
                return false;
            }

            ToastNotification toast = new(document)
            {
                Tag = "missing-reports",
                Group = "amattah"
            };
            toast.Activated += (_, _) => ActivateMainWindow();
            notifier.Show(toast);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatBody(int tripCount, int holidayCount)
    {
        List<string> parts = new();
        if (tripCount > 0)
        {
            parts.Add($"출장 {tripCount}건");
        }

        if (holidayCount > 0)
        {
            parts.Add($"휴일근무 {holidayCount}건");
        }

        return string.Join(" · ", parts);
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private static void ActivateMainWindow()
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Application.Current.MainWindow is not Window window)
            {
                return;
            }

            window.Show();
            window.ShowInTaskbar = true;
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
        }));
    }

    private static void TryCreateStartMenuShortcut()
    {
        string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            exePath = Environment.GetCommandLineArgs().FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return;
        }

        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        Directory.CreateDirectory(programs);
        string shortcutPath = Path.Combine(programs, "아맛다보고서.lnk");
        ShellLink.Create(shortcutPath, Path.GetFullPath(exePath), AppId);
    }

    private static class NativeMethods
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int SetCurrentProcessExplicitAppUserModelID(string appID);
    }
}

internal static class ShellLink
{
    public static void Create(string shortcutPath, string targetPath, string appId, string? arguments = null)
    {
        IShellLinkW link = (IShellLinkW)new CShellLink();
        link.SetPath(targetPath);
        link.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? string.Empty);
        link.SetIconLocation(targetPath, 0);
        link.SetDescription("아맛다보고서");
        link.SetArguments(arguments ?? string.Empty);

        IPropertyStore store = (IPropertyStore)link;
        PropertyKey appIdKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-0E1D5D296C9A"), 5);
        using PropVariant value = PropVariant.String(appId);
        store.SetValue(ref appIdKey, value);
        store.Commit();

        ((IPersistFile)link).Save(shortcutPath, true);
        Marshal.ReleaseComObject(link);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out IntPtr pv);
        void SetValue(ref PropertyKey key, PropVariant pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;

        public PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class PropVariant : IDisposable
    {
        private ushort _vt;
        private ushort _wReserved1;
        private ushort _wReserved2;
        private ushort _wReserved3;
        private IntPtr _data;

        public static PropVariant String(string value)
        {
            return new PropVariant
            {
                _vt = 31, // VT_LPWSTR
                _data = Marshal.StringToCoTaskMemUni(value)
            };
        }

        public void Dispose()
        {
            PropVariantClear(this);
            GC.SuppressFinalize(this);
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear([In, Out] PropVariant pvar);
    }
}
