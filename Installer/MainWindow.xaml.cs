using System.IO;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace Amaranth10Installer;

public partial class MainWindow : Window
{
    private const string ExeName = "아맛다보고서.exe";
    private string _installPath = @"C:\Program Files (x64)\AmathaBogoso\";

    public MainWindow()
    {
        InitializeComponent();
        WelcomeButton.Content = AppVersion.InstallButtonLabel;
        PathBox.Text = _installPath;
    }

    private void Welcome_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel(PathPanel);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using Forms.FolderBrowserDialog dialog = new()
        {
            Description = "설치 경로를 선택하세요",
            SelectedPath = Directory.Exists(PathBox.Text)
                ? PathBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            PathBox.Text = AppendDirectorySeparator(dialog.SelectedPath);
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        string destination = PathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(destination))
        {
            MessageBox.Show(this, "설치 경로를 입력해 주세요.", "아맛다보고서 설치",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _installPath = AppendDirectorySeparator(destination);
        PathBox.Text = _installPath;
        ShowPanel(ProgressPanel);
        SetProgress(0, "프로그램 압축 푸는 중...");

        try
        {
            await Task.Run(() => Installation.InstallTo(_installPath, percent =>
            {
                Dispatcher.Invoke(() => SetProgress(percent, "프로그램 압축 푸는 중..."));
            }));
            SetProgress(100, "설치 완료");
            ShowPanel(DonePanel);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, "설치에 실패했습니다.\n" + exception.Message, "아맛다보고서 설치",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ShowPanel(PathPanel);
        }
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (LaunchCheck.IsChecked == true)
        {
            LaunchInstalledApp();
        }

        Close();
    }

    private void ShowPanel(Grid panel)
    {
        WelcomePanel.Visibility = Visibility.Collapsed;
        PathPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;
        DonePanel.Visibility = Visibility.Collapsed;
        panel.Visibility = Visibility.Visible;
    }

    private void SetProgress(int percent, string status)
    {
        percent = Math.Max(0, Math.Min(100, percent));
        InstallProgress.Value = percent;
        ProgressPercent.Text = percent + "%";
        ProgressStatus.Text = status;
    }

    private void LaunchInstalledApp()
    {
        string exePath = Path.Combine(_installPath, ExeName);
        if (!File.Exists(exePath))
        {
            MessageBox.Show(this, "실행 파일을 찾지 못했습니다.", "아맛다보고서 설치",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Installation.LaunchInstalledApp(exePath, tray: false);
    }

    private static string AppendDirectorySeparator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
               + Path.DirectorySeparatorChar;
    }
}
