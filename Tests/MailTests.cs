using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using Amaranth10API.Services;
using Amaranth10API.Models;
using Amaranth10API.Views;

namespace Amaranth10API.Tests;
internal static class MailTests
{
    private const string Fixture = """
        {"resultCode":0,"resultData":{"code":"0","Result":"OK","TotalRecordCount":201,"TotalUnseenCount":1,"Records":[
        {"muid":123,"subject":"프로젝트 &amp; 일정 확인","fromAddrName":"담당자","fromAddrEmail":"sender@example.com","tooltipDate":"2026-11-13 06:56:58","seen":0,"attach":true},
        {"muid":124,"subject":"","fromAddrName":"","fromAddrEmail":"other@example.com","rfc822date":"11-12 18:32","seen":1,"attach":false}]}}
        """;
    private static void Check(bool okay, string message) { if (!okay) throw new Exception(message); }
    public static void Verify()
    {
        var page = AmaranthClient.ParseInboxResponse(Fixture, 1, 100);
        Check(page.TotalCount == 201 && page.UnreadCount == 1 && page.HasNext && page.Items.Count == 2, "페이지 집계 오류");
        Check(page.Items[0].Subject == "프로젝트 & 일정 확인" && page.Items[0].IsUnread && page.Items[0].HasAttachment, "제목·읽음·첨부 파싱 오류");
        Check(page.Items[1].Subject == "(제목 없음)" && page.Items[1].SenderName == "other@example.com" && !page.Items[1].IsUnread, "기본값 처리 오류");
        Check(!AmaranthClient.ParseInboxResponse(Fixture, 3, 100).HasNext, "마지막 페이지 판정 오류");
        string empty = "{\"resultCode\":0,\"resultData\":{\"code\":\"0\",\"Result\":\"OK\",\"TotalRecordCount\":0,\"TotalUnseenCount\":0,\"Records\":[]}}";
        Check(AmaranthClient.ParseInboxResponse(empty, 1, 100).Items.Count == 0, "빈 목록 처리 오류");
        foreach (string bad in new[] { "{}", "not json", Fixture.Replace("\"resultCode\":0", "\"resultCode\":401"), Fixture.Replace("\"code\":\"0\"", "\"code\":\"401\""), Fixture.Replace("\"Records\":", "\"Missing\":") })
        {
            bool rejected = false;
            try { AmaranthClient.ParseInboxResponse(bad, 1, 100); } catch (InvalidOperationException e) { rejected = !e.Message.Contains("sender@example.com"); }
            Check(rejected, "오류 응답을 빈 받은메일함으로 처리하면 안 됨");
        }
        var handler = new Handler();
        var client = new AmaranthClient();
        typeof(AmaranthClient).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, new HttpClient(handler));
        var session = new AmaranthSession { AuthToken = "test-token", HashKey = "test-key" };
        var result = client.GetInboxAsync(session, 2).GetAwaiter().GetResult();
        Check(result.Page == 2 && handler.Valid, "페이지와 서명 요청 검증 실패");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool stopped = false;
        try { client.GetInboxAsync(session, cancellationToken: cancelled.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { stopped = true; }
        Check(stopped, "조회 취소 처리 오류");
    }
    private sealed class Handler : HttpMessageHandler
    {
        public bool Valid;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string body = await request.Content!.ReadAsStringAsync();
            string id = request.Headers.GetValues("transaction-id").Single();
            string timestamp = request.Headers.GetValues("timestamp").Single();
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("test-key"));
            string signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes("test-token" + id + timestamp + "/mail/mail003A01")));
            Valid = request.Headers.GetValues("wehago-sign").Single() == signature && request.Headers.GetValues("access-domain").Single() == "https://erp.teia.co.kr"
                && body.Contains("\"page\":2") && body.Contains("\"boxName\":\"INBOX\"") && !body.Contains("mboxSeq") && body.Contains("\"seen\":false");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixture) };
        }
    }
    public static void Layout()
    {
        var window = new MailView(new AmaranthClient(), new AmaranthSession());
        var grid = (DataGrid)window.FindName("MailGrid");
        grid.ItemsSource = AmaranthClient.ParseInboxResponse(Fixture, 1, 100).Items;
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1000, 640)); content.Arrange(new Rect(0, 0, 1000, 640)); content.UpdateLayout();
        // DataGrid의 * 열 폭은 첫 배치 뒤에 정해지므로 화면 밖 렌더링에서는 한 번 더 배치합니다.
        content.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
        content.InvalidateMeasure(); content.Measure(new Size(1000, 640)); content.Arrange(new Rect(0, 0, 1000, 640)); content.UpdateLayout();
        Check(grid.Items.Count == 2 && grid.Columns.Count == 2 && grid.IsReadOnly, "메일 목록 XAML 렌더링 오류");
        // 2단 화면: 왼쪽 목록(300~560) 오른쪽에 본문 칸이 있고, 아무것도 고르지 않았으면 안내 문구를 보여 줍니다.
        var list = (FrameworkElement)window.FindName("ListPanel");
        var empty = (FrameworkElement)window.FindName("EmptyBodyPanel");
        Check(grid.Columns[1].ActualWidth > 250, "목록 행이 보낸 사람·제목을 보여 줄 폭을 가져야 함");
        Check(list.ActualWidth >= 270 && list.ActualWidth <= 560 && empty.Visibility == Visibility.Visible
            && empty.TranslatePoint(new Point(0, 0), content).X > list.ActualWidth
            && ((FrameworkElement)window.FindName("BodyHost")).Visibility == Visibility.Collapsed, "목록 오른쪽에 본문 칸(선택 전 안내)이 있어야 함");
        // 미리보기에 선택 행 강조가 보이도록 두 번째 메일을 고른 상태로 찍습니다.
        grid.SelectedIndex = 1;
        content.UpdateLayout();
        var selectedRow = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(1);
        Check(selectedRow.Background is SolidColorBrush selectedBrush && selectedBrush.Color == (Color)ColorConverter.ConvertFromString("#F2F0FB"),
            "선택한 메일 행을 강조해야 함 (IsSelected=" + selectedRow.IsSelected + ", Background=" + selectedRow.Background + ", Source="
            + DependencyPropertyHelper.GetValueSource(selectedRow, Control.BackgroundProperty).BaseValueSource + ")");
        var bitmap = new RenderTargetBitmap(1000, 640, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, "test-data", "mail-preview.png"))) encoder.Save(file);
        window.Dispose();
    }

    public static void EmbeddedNavigation()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var previousBalloon = Amaranth10API.Helpers.WindowsNotification.TrayBalloon;
        var shell = new MainWindow();
        try
        {
            Amaranth10API.Helpers.AppRuntime.Current.Session = new AmaranthSession();
            int windows = Application.Current.Windows.Count;
            typeof(MainWindow).GetMethod("EmailList_Click", flags)!.Invoke(shell, new object[] { shell, new RoutedEventArgs() });
            var host = (ContentControl)shell.FindName("MailHost");
            var view = (MailView)host.Content;
            Check(host.Visibility == Visibility.Visible && Application.Current.Windows.Count == windows, "메일 메뉴는 메인 창에 표시되어야 함");
            var grid = (DataGrid)view.FindName("MailGrid");
            var items = AmaranthClient.ParseInboxResponse(Fixture, 2, 100).Items;
            grid.ItemsSource = items; grid.SelectedItem = items[1];
            typeof(MailView).GetMethod("OpenMail", flags)!.Invoke(view, new object[] { items[1] });
            var bodyHost = (ContentControl)view.FindName("BodyHost");
            var body = (MailBodyView)bodyHost.Content;
            Check(bodyHost.Visibility == Visibility.Visible && Application.Current.Windows.Count == windows, "본문도 새 창 없이 표시되어야 함");
            Check(((FrameworkElement)view.FindName("ListPanel")).Visibility == Visibility.Visible
                && ((FrameworkElement)view.FindName("EmptyBodyPanel")).Visibility == Visibility.Collapsed, "본문을 열어도 목록은 왼쪽에 계속 보여야 함");
            typeof(MailView).GetMethod("OpenMail", flags)!.Invoke(view, new object[] { items[0] });
            Check(ReferenceEquals(bodyHost.Content, body) && body.MailId == items[0].Id, "다른 메일은 같은 본문 칸(같은 웹뷰어)에서 열어야 함");
            ((Button)body.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(bodyHost.Content == null && ((FrameworkElement)view.FindName("EmptyBodyPanel")).Visibility == Visibility.Visible
                && ReferenceEquals(grid.ItemsSource, items) && ReferenceEquals(grid.SelectedItem, items[1]), "본문 닫기 후 안내 표시, 데이터와 선택 유지");
            Check((bool)typeof(MailBodyView).GetField("_closed", flags)!.GetValue(body)!, "복귀한 본문 리소스 해제");
            typeof(MainWindow).GetMethod("ShowDashboard", flags)!.Invoke(shell, null);
            Check(host.Visibility == Visibility.Collapsed, "미작성 목록으로 전환");
            typeof(MainWindow).GetMethod("EmailList_Click", flags)!.Invoke(shell, new object[] { shell, new RoutedEventArgs() });
            Check(ReferenceEquals(host.Content, view) && ReferenceEquals(grid.SelectedItem, items[1]), "메일 재진입 상태 유지");
            typeof(MainWindow).GetMethod("Settings_Click", flags)!.Invoke(shell, new object[] { shell, new RoutedEventArgs() });
            Check(host.Visibility == Visibility.Collapsed, "설정 화면이 웹뷰어에 가려지면 안 됨");
            typeof(MainWindow).GetMethod("EmailList_Click", flags)!.Invoke(shell, new object[] { shell, new RoutedEventArgs() });
            Check(host.Visibility == Visibility.Visible, "설정에서 메일 복귀");

            // 사용자 매뉴얼도 새 창 없이 같은 창의 패널로 열려야 함
            var manualHost = (ContentControl)shell.FindName("ManualHost");
            var navManual = (RadioButton)shell.FindName("NavManual");
            Check(ManualView.Exists(ManualView.DefaultFolder), "테스트 출력 폴더에 ReadMe.html이 있어야 함");
            typeof(MainWindow).GetMethod("Manual_Click", flags)!.Invoke(shell, new object[] { shell, new RoutedEventArgs() });
            var manual = manualHost.Content as ManualView;
            Check(manual != null && manualHost.Visibility == Visibility.Visible && host.Visibility == Visibility.Collapsed
                && navManual.IsChecked == true && Application.Current.Windows.Count == windows, "매뉴얼은 새 창 없이 메인 창 안에 표시되어야 함");
            typeof(MainWindow).GetMethod("Settings_Click", flags)!.Invoke(shell, new object[] { shell, new RoutedEventArgs() });
            Check(manualHost.Visibility == Visibility.Collapsed, "설정 화면이 매뉴얼 웹뷰어에 가려지면 안 됨");
            typeof(MainWindow).GetMethod("ShowDashboard", flags)!.Invoke(shell, null);
            Check(manualHost.Visibility == Visibility.Collapsed && navManual.IsChecked != true, "미작성 목록으로 가면 매뉴얼을 숨겨야 함");
            typeof(MainWindow).GetMethod("Manual_Click", flags)!.Invoke(shell, new object[] { shell, new RoutedEventArgs() });
            Check(ReferenceEquals(manualHost.Content, manual) && manualHost.Visibility == Visibility.Visible, "매뉴얼 재진입 시 읽던 화면을 유지해야 함");
            shell.NavigateToLogin();
            Check(manualHost.Content == null && (bool)typeof(ManualView).GetField("_closed", flags)!.GetValue(manual)!, "로그아웃 시 매뉴얼 리소스 해제");
            Check(host.Content == null && (bool)typeof(MailView).GetField("_closed", flags)!.GetValue(view)!, "로그아웃 시 메일 리소스 해제");
        }
        finally
        {
            typeof(MainWindow).GetField("_forceClose", flags)!.SetValue(shell, true);
            shell.Close();
            Amaranth10API.Helpers.WindowsNotification.TrayBalloon = previousBalloon;
        }
    }
}
