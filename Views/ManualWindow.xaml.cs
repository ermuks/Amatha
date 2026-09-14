using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Amaranth10API.Helpers;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Amaranth10API.Views;

public partial class ManualWindow : Window
{
    private const string HostName = "app.manual";
    private readonly string _folder;
    private readonly string _htmlPath;
    private readonly WebView2 _browser = new();

    public ManualWindow(string htmlPath)
    {
        _htmlPath = htmlPath;
        _folder = Path.GetDirectoryName(htmlPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        InitializeComponent();
        ApplyWindowIcon();
        PlaceOnScreen();
        BrowserHost.Children.Insert(0, _browser);
        Loaded += Window_Loaded;
    }

    public static void Open(Window? owner)
    {
        string folder = AppDomain.CurrentDomain.BaseDirectory;
        string htmlPath = Path.Combine(folder, "ReadMe.html");
        if (!File.Exists(htmlPath))
        {
            MessageBox.Show(
                owner,
                "사용 매뉴얼 파일(ReadMe.html)을 찾을 수 없습니다.\n프로그램이 있는 폴더를 확인해 주세요.",
                "사용자 매뉴얼",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ManualWindow window = new(htmlPath);
        if (owner != null)
        {
            window.Owner = owner;
        }

        window.Show();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= Window_Loaded;
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "아맛다보고서",
                "WebView2Manual");
            Directory.CreateDirectory(userData);

            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userData);
            await _browser.EnsureCoreWebView2Async(environment);

            CoreWebView2 core = _browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsZoomControlEnabled = true;
            core.SetVirtualHostNameToFolderMapping(
                HostName,
                _folder,
                CoreWebView2HostResourceAccessKind.Allow);
            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess && LoadingOverlay.Visibility == Visibility.Visible)
                {
                    LoadingText.Text = "사용 매뉴얼을 열지 못했습니다.";
                    return;
                }

                LoadingOverlay.Visibility = Visibility.Collapsed;
            };
            core.Navigate($"https://{HostName}/{Path.GetFileName(_htmlPath)}");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            LoadingText.Text = "매뉴얼을 보려면 Microsoft Edge WebView2 Runtime이 필요합니다.";
        }
        catch (Exception exception)
        {
            LoadingText.Text = exception.Message;
        }
    }

    private void ApplyWindowIcon()
    {
        try
        {
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIcon.ico", UriKind.Absolute));
        }
        catch
        {
            if (Application.Current?.MainWindow?.Icon != null)
            {
                Icon = Application.Current.MainWindow.Icon;
            }
        }
    }

    private void PlaceOnScreen()
    {
        Rect work = ScreenPlacement.WorkingAreaDip();
        Width = Math.Min(980, Math.Max(640, work.Width - 48));
        Height = Math.Min(work.Height - 32, Math.Max(480, work.Height * 0.86));
        Left = work.Left + Math.Max(0, (work.Width - Width) / 2);
        Top = work.Top + Math.Max(0, (work.Height - Height) / 2);
        WindowStartupLocation = WindowStartupLocation.Manual;
    }
}
