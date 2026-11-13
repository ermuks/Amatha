using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Amaranth10API.Helpers;
using Amaranth10API.Models;
using Amaranth10API.Services;
using Amaranth10API.ViewModels;
using Amaranth10API.Views;

namespace Amaranth10API.Tests;

/// <summary>
/// 사용 매뉴얼(docs/images)에 넣을 화면을 실제 창으로 띄워 찍습니다.
///   Amaranth10API.Tests.exe --manual-shots &lt;저장 폴더&gt;
/// 자동 로그인을 끄고 ERP 연결을 로컬 가짜 응답으로 바꿔, 실제 계정·데이터를 쓰지 않습니다(이름·부서·메일은 모두 가상).
/// 각 화면 PNG와 함께, 매뉴얼의 빨간 표시를 그릴 요소 위치를 rects.txt(이미지 픽셀, 96 DPI 기준)로 남깁니다.
/// </summary>
internal static class ManualScreenshots
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly StringBuilder Rects = new();
    private static string _out = string.Empty;

    public static int Run(string outputFolder)
    {
        _out = Path.GetFullPath(outputFolder);
        Directory.CreateDirectory(_out);
        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        int result = 1;
        var frame = new DispatcherFrame();
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await CaptureAllAsync();
                File.WriteAllText(Path.Combine(_out, "rects.txt"), Rects.ToString(), Encoding.UTF8);
                Console.WriteLine("SHOTS " + _out);
                result = 0;
            }
            catch (Exception error)
            {
                Console.WriteLine("FAIL manual shots: " + error);
            }
            finally
            {
                frame.Continue = false;
            }
        }));
        Dispatcher.PushFrame(frame);
        // 닫은 WebView2들이 CLR 종료 과정의 COM 정리에서 예외를 내므로(Environment.Exit도 마찬가지),
        // 결과를 다 쓴 뒤 정리 단계를 건너뛰고 종료 코드만 남긴 채 바로 끝냅니다. 화면 찍기 전용 모드라 괜찮습니다.
        Console.Out.Flush();
        Native.TerminateProcess(Native.GetCurrentProcess(), (uint)result);
        return result;
    }

    private static async Task CaptureAllAsync()
    {
        GoOffline(App.Client);
        var session = new AmaranthSession
        {
            AuthToken = "fixture", HashKey = "fixture-key", CompanySequence = "company-1", EmployeeSequence = "employee-1",
            EmployeeName = "김테이아", DepartmentName = "디지털트윈팀"
        };

        var window = new MainWindow();
        // 저장된 계정으로 자동 로그인하지 않도록 Loaded 처리기를 뗍니다.
        MethodInfo autoLogin = typeof(MainWindow).GetMethod("MainWindow_Loaded", Private)!;
        window.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), window, autoLogin);
        ((DispatcherTimer)typeof(MainWindow).GetField("_updateTimer", Private)!.GetValue(window)!).Stop();
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = 40; window.Top = 40; window.Width = 1200; window.Height = 780;
        window.Show();
        window.Activate();
        try
        {
            // 1. 로그인
            await Settle(1200);
            var login = (LoginPage)((Frame)window.FindName("RootFrame")).Content;
            Capture(window, "01-login.png");
            Mark(window, "01-login", "id", (FrameworkElement)login.FindName("IdBox"));
            Mark(window, "01-login", "password", (FrameworkElement)login.FindName("PasswordBox"));
            Mark(window, "01-login", "button", Descendants<Button>(login).First(b => Equals(b.Content, "로그인")));

            // 2. 미작성 목록 (가상 일정)
            AppRuntime.Current.Session = session;
            window.NavigateToDashboard(session);
            await Settle(800);
            var page = (DashboardPage)((Frame)window.FindName("RootFrame")).Content;
            DashboardViewModel dashboard = FixtureDashboard(session);
            page.DataContext = dashboard;
            typeof(MainWindow).GetMethod("UpdateDashboardBadge", Private)!.Invoke(window, new object[] { dashboard.TotalCount });
            await Settle(900);
            Capture(window, "02-dashboard.png");
            Mark(window, "02-dashboard", "sidebar", (FrameworkElement)window.FindName("SidePanel"));
            Mark(window, "02-dashboard", "banner", CardOf(FindText(page, t => t.StartsWith("작성할 보고서가", StringComparison.Ordinal))));
            Mark(window, "02-dashboard", "refresh", Descendants<Button>(page).First(b => Descendants<TextBlock>(b).Any(t => t.Text == "새로고침") || Equals(b.Content, "새로고침")));
            var rows = Descendants<Button>(page).Where(b => b.DataContext is MissingPeriodViewModel && Descendants<CheckBox>(b).Any()).ToList();
            if (rows.Count == 0) rows = Descendants<Button>(page).Where(b => b.DataContext is MissingPeriodViewModel).ToList();
            for (int i = 0; i < Math.Min(3, rows.Count); i++) Mark(window, "02-dashboard", "row" + i, rows[i]);
            var checks = Descendants<CheckBox>(page).ToList();
            var deletes = Descendants<Button>(page).Where(b => Equals(b.Content, "삭제")).ToList();
            for (int i = 0; i < Math.Min(3, checks.Count); i++) Mark(window, "02-dashboard", "snooze" + i, checks[i]);
            for (int i = 0; i < Math.Min(3, deletes.Count); i++) Mark(window, "02-dashboard", "delete" + i, deletes[i]);

            // 2-1. 신청서 보기 (항목을 누르면 열리는 창, 가상 신청서)
            await CaptureApplicationAsync();

            // 3. 메일 (2단: 왼쪽 목록 + 오른쪽 본문)
            string profile = Path.Combine(AppContext.BaseDirectory, "test-data", "manual-webview-" + Guid.NewGuid().ToString("N"));
            typeof(MailBodyView).GetField("_environment", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, CoreWebView2Environment.CreateAsync(null, profile));
            var mailClient = new AmaranthClient();
            SetHttp(mailClient, new MailFixtureHandler());
            var mailView = new MailView(mailClient, session);
            mailView.UnreadCountChanged += count => typeof(MainWindow).GetMethod("UpdateMailBadge", Private)!.Invoke(window, new object[] { count });
            typeof(MainWindow).GetField("_mailView", Private)!.SetValue(window, mailView);
            ((ContentControl)window.FindName("MailHost")).Content = mailView;
            typeof(MainWindow).GetMethod("EmailList_Click", Private)!.Invoke(window, new object[] { window, new RoutedEventArgs() });
            var grid = (DataGrid)mailView.FindName("MailGrid");
            for (int i = 0; i < 50 && grid.Items.Count == 0; i++) await Task.Delay(100);
            var first = (InboxMail)grid.Items[0];
            grid.SelectedItem = first;
            typeof(MailView).GetMethod("OpenMail", Private)!.Invoke(mailView, new object[] { first });
            await Settle(3500);
            Capture(window, "04-mail.png");
            Mark(window, "04-mail", "nav-mail", (FrameworkElement)window.FindName("NavMail"));
            Mark(window, "04-mail", "list", (FrameworkElement)mailView.FindName("ListPanel"));
            Mark(window, "04-mail", "search", (FrameworkElement)mailView.FindName("SearchBox"));
            Mark(window, "04-mail", "body", (FrameworkElement)mailView.FindName("BodyHost"));
            if (grid.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement row0) Mark(window, "04-mail", "selected-row", row0);
            var body = (MailBodyView)((ContentControl)mailView.FindName("BodyHost")).Content;
            Mark(window, "04-mail", "attachments", (FrameworkElement)body.FindName("AttachmentPanel"));
            Mark(window, "04-mail", "download-all", (FrameworkElement)body.FindName("DownloadAllButton"));
            Mark(window, "04-mail", "close", (FrameworkElement)body.FindName("BackButton"));

            // 4. 환경설정
            typeof(MainWindow).GetMethod("Settings_Click", Private)!.Invoke(window, new object[] { window, new RoutedEventArgs() });
            await Settle(700);
            var settings = (FrameworkElement)window.FindName("SettingsOverlay");
            Capture(window, "05-settings.png");
            Mark(window, "05-settings", "nav-settings", (FrameworkElement)window.FindName("NavSettings"));
            foreach (string title in new[] { "알림", "시작", "세션", "조회 범위" })
            {
                TextBlock? header = FindTextOrNull(settings, t => t == title || t.EndsWith("보고서 " + title, StringComparison.Ordinal));
                if (header != null) Mark(window, "05-settings", "card-" + title, CardOf(header));
            }

            // 5. 종료 확인 (미작성 목록 위)
            typeof(MainWindow).GetMethod("ShowDashboard", Private)!.Invoke(window, null);
            await Settle(500);
            var dialogHost = (ConfirmDialog)window.FindName("ConfirmDialogHost");
            MethodInfo showConfirm = typeof(MainWindow).GetMethod("ShowConfirm", Private)!;
            _ = window.Dispatcher.BeginInvoke(new Action(() => showConfirm.Invoke(window, new object?[] { "종료하기", "정말로 종료하시겠습니까?", null, false })));
            await Settle(900);
            Capture(window, "07-exit.png");
            Mark(window, "07-exit", "dialog", (FrameworkElement)dialogHost.FindName("DialogCard"));
            Mark(window, "07-exit", "yes", Descendants<Button>(dialogHost).First(b => Equals(b.Content, "예")));
            dialogHost.Complete(false);
            await Settle(300);

            // 6. 사용자 매뉴얼 (같은 창 안)
            if (ManualView.Exists(ManualView.DefaultFolder))
            {
                typeof(MainWindow).GetMethod("Manual_Click", Private)!.Invoke(window, new object[] { window, new RoutedEventArgs() });
                await Settle(3000);
                Capture(window, "08-manual.png");
                Mark(window, "08-manual", "nav-manual", (FrameworkElement)window.FindName("NavManual"));
            }

            // 7. 트레이 메뉴 (투명 창이라 WPF로 그림)
            var tray = new TrayMenuWindow { Left = 300, Top = 300, ShowActivated = false };
            tray.Show();
            await Settle(500);
            // 창 전체(그림자 여백 포함)를 그립니다. 내용 요소만 그리면 여백만큼 잘림
            RenderElement(tray, "06-tray.png", 2);
            foreach (Button item in Descendants<Button>(tray))
            {
                string label = string.Join("", Descendants<TextBlock>(item).Select(t => t.Text));
                if (label.Length == 0) label = item.Content?.ToString() ?? "";
                MarkInside(tray, "06-tray", "item-" + label, item, 2);
            }
            tray.Close();
        }
        finally
        {
            typeof(MainWindow).GetField("_forceClose", Private)!.SetValue(window, true);
            window.Close();
            Toast.DismissAll();
        }
    }

    private static async Task CaptureApplicationAsync()
    {
        string profile = Path.Combine(AppContext.BaseDirectory, "test-data", "manual-report-" + Guid.NewGuid().ToString("N"));
        typeof(ReportBrowserWindow).GetField("SharedEnvironment", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, await CoreWebView2Environment.CreateAsync(null, profile));

        DateTime start = DateTime.Today.AddDays(9), end = DateTime.Today.AddDays(13);
        string Day(DateTime d) => $"{d.Year}년 {d.Month}월 {d.Day}일";
        string th = "style=\"background:#f3f5f8;padding:8px 10px;border:1px solid #c9d1db;width:16%;text-align:center;font-weight:600\"";
        string td = "style=\"padding:8px 10px;border:1px solid #c9d1db\"";
        string contents =
            "<h2 style=\"text-align:center;margin:8px 0 18px;font-size:22px\">출장신청서</h2>" +
            "<table style=\"border-collapse:collapse;width:100%;font-size:13px\">" +
            $"<tr><th {th}>문서번호</th><td {td}>TEIA-2026-1107</td><th {th}>기안일</th><td {td}>{Day(DateTime.Today.AddDays(-2))}</td></tr>" +
            $"<tr><th {th}>기안자</th><td {td}>김테이아</td><th {th}>부서</th><td {td}>디지털트윈팀</td></tr>" +
            $"<tr><th {th}>출장구분</th><td {td}>해외출장</td><th {th}>숙박</th><td {td}>숙박</td></tr>" +
            $"<tr><th {th}>출장기간</th><td {td} colspan=\"3\">{Day(start)} ~ {Day(end)} (5일)</td></tr>" +
            $"<tr><th {th}>출장지</th><td {td} colspan=\"3\">해외 전시회장</td></tr>" +
            $"<tr><th {th}>출장목적</th><td {td} colspan=\"3\">해외 전시회 참관 및 기술 동향 조사</td></tr>" +
            $"<tr><th {th}>동행자</th><td {td} colspan=\"3\">없음</td></tr>" +
            "</table>";
        var document = new BusinessTripDocument
        {
            Title = "[출장신청] 해외 전시회 참관", DocumentNumber = "TEIA-2026-1107", DocumentStatus = "종결",
            TripStartDate = start, TripEndDate = end, DocContents = contents
        };

        var window = new ReportBrowserWindow(new AmaranthSession(), applicationDocument: document)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = 80, Top = 60, Width = 1000, Height = 680, ShowActivated = true
        };
        window.Show();
        try
        {
            await Settle(3500);
            Capture(window, "03-application.png");
            Mark(window, "03-application", "address", (FrameworkElement)window.FindName("AddressBox"));
            Mark(window, "03-application", "content", (FrameworkElement)window.FindName("BrowserHost"));
        }
        finally
        {
            window.Close();
        }
    }

    // ───────── 가상 데이터 ─────────

    private static DashboardViewModel FixtureDashboard(AmaranthSession session)
    {
        DateTime today = DateTime.Today;
        var client = new AmaranthClient();
        GoOffline(client);

        void Trip(DateTime start, DateTime end, string name, string title, string projectCode, int id)
        {
            for (DateTime d = start; d <= end; d = d.AddDays(1))
                client.Schedules.Add(new WorkSchedule { Date = d, Name = name, FormName = "출장신청서", Title = title });
            client.BusinessTripDocuments.Add(new BusinessTripDocument
            {
                DocumentId = id, Title = title, DocumentNumber = "TRIP-" + id, DocumentStatus = "종결", ProjectCode = projectCode,
                TripStartDate = start, TripEndDate = end,
                CoveredDates = Enumerable.Range(0, (end - start).Days + 1).Select(i => start.AddDays(i)).ToList()
            });
        }

        Trip(today.AddDays(-24), today.AddDays(-20), "국내출장", "[출장신청] 고객사 현장 점검", "PJ-2026-031", 101);
        Trip(today.AddDays(-1), today.AddDays(3), "국내출장", "[출장신청] 디지털트윈 데모 지원", "PJ-2026-044", 102);
        Trip(today.AddDays(9), today.AddDays(13), "해외출장", "[출장신청] 해외 전시회 참관", string.Empty, 103);

        DateTime holiday = today.AddDays(-9);
        client.Schedules.Add(new WorkSchedule { Date = holiday, Name = "휴일근무", FormName = "휴일근무신청서", Title = "[휴일근무신청] 서버 점검 대응" });
        client.HolidayWorkDocuments.Add(new BusinessTripDocument
        {
            DocumentId = 201, Title = "[휴일근무신청] 서버 점검 대응", DocumentNumber = "HOLI-201", DocumentStatus = "종결",
            TripStartDate = holiday, TripEndDate = holiday, StartTime = "09:00", EndTime = "15:00",
            WorkDays = new() { new HolidayWorkDay { WorkDate = holiday, StartTime = "09:00", EndTime = "15:00" } }
        });

        DateTime substitute = today.AddDays(-16);
        client.BusinessTripReports.Add(new BusinessTripReport
        {
            Title = "[휴일근무보고] 정기 배포 지원", TripStartDate = substitute, TripEndDate = substitute,
            HolidayWorks = new() { new HolidayWorkEntry { WorkDate = substitute, CompensationType = "대체휴무", StartTime = "10:00", EndTime = "16:00" } }
        });

        string snoozeFile = Path.Combine(AppContext.BaseDirectory, "test-data", "manual-snooze-" + Guid.NewGuid().ToString("N") + ".json");
        var dashboard = new DashboardViewModel(client, new NotificationSnoozeStore(snoozeFile));
        typeof(DashboardViewModel).GetField("_session", Private)!.SetValue(dashboard, session);
        typeof(DashboardViewModel).GetField("_isBusy", Private)?.SetValue(dashboard, false);
        typeof(DashboardViewModel).GetField("_lastLoadedAt", Private)?.SetValue(dashboard, (DateTime?)DateTime.Now);
        typeof(DashboardViewModel).GetMethod("ApplyMissingPeriods", Private)!.Invoke(dashboard, null);
        dashboard.RefreshRelativeTimes();
        return dashboard;
    }

    private sealed class MailFixtureHandler : HttpMessageHandler
    {
        private static readonly (long Id, string Subject, string Name, string Email, int AgoMinutes, bool Unread, bool Attach)[] Mails =
        {
            (501, "[결재 완료] 해외출장 신청이 승인되었습니다", "ERP 알림", "noreply@erp.example.com", 30, true, true),
            (502, "11월 출장비 정산 양식 안내", "박지민", "jimin.park@example.com", 60 * 20, true, true),
            (503, "11/15 휴일근무 일정 공유드립니다", "이도윤", "doyun.lee@example.com", 60 * 48, false, false),
            (504, "[안내] 대체휴가 사용 기한을 확인해 주세요", "ERP 알림", "noreply@erp.example.com", 60 * 72, false, false),
            (505, "하반기 업무 보고서 작성 가이드", "인사팀", "hr@example.com", 60 * 24 * 6, false, true),
            (506, "법인카드 사용 내역 제출 요청", "경영지원팀", "finance@example.com", 60 * 24 * 7, false, true),
            (507, "[결재 완료] 휴일근무 신청이 승인되었습니다", "ERP 알림", "noreply@erp.example.com", 60 * 24 * 8, false, false),
            (508, "디지털트윈 데모 일정 확정", "최서준", "seojun.choi@example.com", 60 * 24 * 9, false, false),
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = request.RequestUri!.AbsolutePath;
            string body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync();
            if (path == "/mail/mail003A01")
            {
                var records = Mails.Select(m => new
                {
                    muid = m.Id, subject = m.Subject, fromAddrName = m.Name, fromAddrEmail = m.Email,
                    tooltipDate = DateTime.Now.AddMinutes(-m.AgoMinutes).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    seen = m.Unread ? 0 : 1, attach = m.Attach
                });
                string list = JsonSerializer.Serialize(new { resultCode = 0, resultData = new { code = "0", Result = "OK", TotalRecordCount = Mails.Length, TotalUnseenCount = Mails.Count(m => m.Unread), Records = records } });
                return Json(list);
            }

            if (path == "/mail/mail002A01")
            {
                long id = long.Parse(Regex.Match(body, "\"uid\"\\s*:\\s*\"?(\\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
                var mail = Mails.First(m => m.Id == id);
                string html = "<p>김테이아 님,</p><p>신청하신 해외출장이 최종 승인되었습니다.</p><p>· 기간: 해외 전시회 참관 일정<br>· 소속: 디지털트윈팀</p><p>출장이 끝나면 출장 보고서를 제출해 주세요.</p>";
                var files = mail.Attach
                    ? new object[]
                    {
                        new { originalFileName = "출장신청서_승인본.pdf", fileSize = "253952", fileSn = "file-1" },
                        new { originalFileName = "출장_일정표.hwp", fileSize = "90112", fileSn = "file-2" }
                    }
                    : new object[0];
                string detail = JsonSerializer.Serialize(new
                {
                    resultCode = 0,
                    resultData = new
                    {
                        code = "0", muid = id,
                        mime = new { body = new { html, plain = "" }, fileList = files },
                        decodeMime = new
                        {
                            subject = mail.Subject, from = mail.Name + " &lt;" + mail.Email + "&gt;", to = "김테이아 &lt;teia.kim@example.com&gt;",
                            date = DateTime.Now.AddMinutes(-mail.AgoMinutes).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                            email = "teia.kim@example.com", empSeq = "employee-1"
                        }
                    }
                });
                return Json(detail);
            }

            throw new HttpRequestException("manual screenshot fixture: offline");
        }

        private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("manual screenshot fixture: offline");
    }

    private static void GoOffline(AmaranthClient client) => SetHttp(client, new OfflineHandler());

    private static void SetHttp(AmaranthClient client, HttpMessageHandler handler)
    {
        foreach (FieldInfo field in typeof(AmaranthClient).GetFields(Private).Where(f => f.FieldType == typeof(HttpClient)))
            field.SetValue(client, new HttpClient(handler));
    }

    // ───────── 찍기 ─────────

    private static async Task Settle(int milliseconds)
    {
        await Task.Delay(milliseconds);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>창 전체(제목 표시줄 포함)를 찍어 96 DPI 크기로 저장합니다. WebView2 영역까지 찍히도록 PrintWindow를 씁니다.</summary>
    private static void Capture(Window window, string fileName)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        Native.GetWindowRect(handle, out Native.Rect outer);
        Native.Rect frame = Bounds(handle);
        using var full = new System.Drawing.Bitmap(outer.Right - outer.Left, outer.Bottom - outer.Top, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(full))
        {
            IntPtr hdc = g.GetHdc();
            Native.PrintWindow(handle, hdc, 2); // PW_RENDERFULLCONTENT
            g.ReleaseHdc(hdc);
        }

        var crop = new System.Drawing.Rectangle(frame.Left - outer.Left, frame.Top - outer.Top, frame.Right - frame.Left, frame.Bottom - frame.Top);
        using var cropped = full.Clone(crop, full.PixelFormat);
        double scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
        int width = (int)Math.Round(cropped.Width / scale), height = (int)Math.Round(cropped.Height / scale);
        using var output = new System.Drawing.Bitmap(width, height);
        using (var g = System.Drawing.Graphics.FromImage(output))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(cropped, 0, 0, width, height);
        }

        output.Save(Path.Combine(_out, fileName), ImageFormat.Png);
        Rects.AppendLine($"image {Path.GetFileNameWithoutExtension(fileName)} {width} {height}");
        Console.WriteLine($"SHOT {fileName} {width}x{height} (dpi x{scale:0.##})");
    }

    private static Native.Rect Bounds(IntPtr handle)
    {
        if (Native.DwmGetWindowAttribute(handle, 9, out Native.Rect rect, Marshal.SizeOf<Native.Rect>()) == 0) return rect;
        Native.GetWindowRect(handle, out rect);
        return rect;
    }

    /// <summary>요소 위치를 찍은 이미지 좌표(96 DPI)로 기록합니다.</summary>
    private static void Mark(Window window, string image, string name, FrameworkElement? element)
    {
        if (element == null || !element.IsVisible) { Rects.AppendLine($"rect {image} {name} missing"); return; }
        Native.Rect frame = Bounds(new WindowInteropHelper(window).Handle);
        double scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
        Point screen = element.PointToScreen(new Point(0, 0));
        double x = (screen.X - frame.Left) / scale, y = (screen.Y - frame.Top) / scale;
        Rects.AppendLine(string.Format(CultureInfo.InvariantCulture, "rect {0} {1} {2:0} {3:0} {4:0} {5:0}", image, name, x, y, element.ActualWidth, element.ActualHeight));
    }

    private static void MarkInside(FrameworkElement root, string image, string name, FrameworkElement element, double scale)
    {
        Point p = element.TranslatePoint(new Point(0, 0), root);
        Rects.AppendLine(string.Format(CultureInfo.InvariantCulture, "rect {0} {1} {2:0} {3:0} {4:0} {5:0}", image, name.Replace(' ', '_'),
            p.X * scale, p.Y * scale, element.ActualWidth * scale, element.ActualHeight * scale));
    }

    private static void RenderElement(FrameworkElement element, string fileName, double scale)
    {
        int width = (int)Math.Ceiling(element.ActualWidth * scale), height = (int)Math.Ceiling(element.ActualHeight * scale);
        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream stream = File.Create(Path.Combine(_out, fileName))) encoder.Save(stream);
        Rects.AppendLine($"image {Path.GetFileNameWithoutExtension(fileName)} {width} {height}");
        Console.WriteLine($"SHOT {fileName} {width}x{height}");
    }

    // ───────── 시각 트리 ─────────

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (T nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static TextBlock? FindTextOrNull(DependencyObject root, Func<string, bool> match) =>
        Descendants<TextBlock>(root).FirstOrDefault(t => t.IsVisible && match(t.Text ?? string.Empty));

    private static TextBlock FindText(DependencyObject root, Func<string, bool> match) =>
        FindTextOrNull(root, match) ?? throw new InvalidOperationException("화면에서 글자를 찾지 못했습니다.");

    /// <summary>글자를 감싼 가장 가까운 카드(둥근 모서리 테두리)</summary>
    private static FrameworkElement CardOf(FrameworkElement element)
    {
        DependencyObject? current = element;
        while (current != null)
        {
            if (current is Border border && border.CornerRadius.TopLeft >= 10 && (border.BorderThickness.Left > 0 || border.Background != null)
                && border.ActualWidth > element.ActualWidth + 40)
                return border;
            current = VisualTreeHelper.GetParent(current);
        }

        return element;
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Rect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] public static extern bool TerminateProcess(IntPtr process, uint exitCode);
    }
}
