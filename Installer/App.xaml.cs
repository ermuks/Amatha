using System.IO;
using System.Windows;
using Amaranth10API.Helpers;

namespace Amaranth10Installer;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            string? executablePath = SilentUpdateOptions.Parse(e.Args);
            if (executablePath == null)
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                new MainWindow().Show();
                return;
            }

            await Task.Run(() => Installation.InstallTo(Path.GetDirectoryName(executablePath)!, _ => { }));
            Installation.LaunchInstalledApp(executablePath, tray: true);
            Shutdown(0);
        }
        catch (Exception exception)
        {
            MessageBox.Show("업데이트에 실패했습니다.\n" + exception.Message, "아맛다보고서 설치",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
