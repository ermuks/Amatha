using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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

internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--webview-smoke")) return WebViewSmoke();
        Run("2시간 직전과 정확한 만료 시점", ExpiryBoundary);
        Run("해제와 재체크의 만료 시간", UncheckAndRecheck);
        Run("재실행 후 남은 시간 유지와 해제 저장", Persistence);
        Run("손상 파일과 저장 실패 시 메모리 동작", StorageFailure);
        Run("카드별 알림 중지와 바인딩 변경 통지", ItemNotifications);
        Run("새로고침 후 체크 유지와 만료 해제", DashboardReload);
        Run("사용자·종류·날짜별 상태 분리", KeyIsolation);
        Run("알림 집계·해제·만료 후 다음 사이클", NotificationCycles);
        Run("체크박스 클릭은 보고서 창을 열지 않음", CheckBoxRouting);
        Run("한 자리 월·일과 유효하지 않은 날짜", FlexibleDateParsing);
        Run("대체휴가 사용예정일을 근무일로 인식하지 않음", SubstituteDateRegression);
        Run("휴가 사용 문서는 보고서·요청서 대상으로 분류하지 않음", LeaveUsageClassification);
        Run("카드가 기존 신청서 원문을 보관함", ApplicationDocumentLink);
        Run("신청서 HTML과 텍스트 원문 표시", ApplicationHtml);
        Run("실제 대시보드 XAML 체크박스와 화면 렌더링", DashboardLayout);
        Console.WriteLine($"RESULT: {_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { _failed++; Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpiryBoundary()
    {
        DateTimeOffset now = Start;
        var store = new NotificationSnoozeStore(utcNow: () => now);
        Assert(!store.IsSnoozed("item"), "새 항목은 활성 상태여야 합니다.");
        store.SetSnoozed("item", true);
        now = Start.AddHours(2).AddTicks(-1);
        Assert(store.IsSnoozed("item"), "2시간 직전에는 중지되어야 합니다.");
        now = Start.AddHours(2);
        Assert(!store.IsSnoozed("item"), "정확히 2시간 뒤 만료되어야 합니다.");
        now = Start.AddHours(3);
        Assert(!store.IsSnoozed("item"), "만료 후에는 중지가 유지되면 안 됩니다.");
    }

    private static void UncheckAndRecheck()
    {
        DateTimeOffset now = Start;
        var store = new NotificationSnoozeStore(utcNow: () => now);
        store.SetSnoozed("item", true);
        now = Start.AddMinutes(30);
        store.SetSnoozed("item", false);
        Assert(!store.IsSnoozed("item"), "해제 후 즉시 알림 대상이어야 합니다.");
        now = Start.AddHours(1);
        store.SetSnoozed("item", true);
        now = Start.AddHours(2);
        Assert(store.IsSnoozed("item"), "재체크는 새 2시간을 시작해야 합니다.");
        now = Start.AddHours(3);
        Assert(!store.IsSnoozed("item"), "재체크 후 2시간에 만료되어야 합니다.");
    }

    private static string TestPath() => Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"), "snoozes.json");

    private static void Persistence()
    {
        DateTimeOffset now = Start;
        string path = TestPath();
        var store = new NotificationSnoozeStore(path, () => now);
        store.SetSnoozed("item", true);
        Assert(File.Exists(path), "중지 상태가 저장되어야 합니다.");
        now = Start.AddMinutes(90);
        var restored = new NotificationSnoozeStore(path, () => now);
        Assert(restored.IsSnoozed("item"), "재실행 후 남은 30분이 유지되어야 합니다.");
        now = Start.AddHours(2);
        Assert(!restored.IsSnoozed("item"), "재실행이 만료 시간을 연장하면 안 됩니다.");
        restored.SetSnoozed("second", true);
        Assert(new NotificationSnoozeStore(path, () => now).IsSnoozed("second"), "기존 저장 파일에 새 체크도 저장되어야 합니다.");
        restored.SetSnoozed("second", false);
        Assert(!new NotificationSnoozeStore(path, () => now).IsSnoozed("second"), "체크 해제도 저장되어야 합니다.");
        Assert(!File.Exists(path + ".tmp"), "저장 후 불필요한 임시 파일이 남으면 안 됩니다.");
    }

    private static void StorageFailure()
    {
        string path = TestPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "broken json");
        var corrupted = new NotificationSnoozeStore(path, () => Start);
        Assert(!corrupted.IsSnoozed("item"), "손상된 파일은 알림을 막으면 안 됩니다.");
        var unwritable = new NotificationSnoozeStore(Path.GetDirectoryName(path), () => Start);
        unwritable.SetSnoozed("item", true);
        Assert(unwritable.IsSnoozed("item"), "저장 실패 시에도 현재 체크는 유효해야 합니다.");
        unwritable.SetSnoozed("item", false);
        Assert(!unwritable.IsSnoozed("item"), "저장 실패 시 체크 해제가 가능해야 합니다.");
    }

    private static void ItemNotifications()
    {
        DateTimeOffset now = Start;
        var store = new NotificationSnoozeStore(utcNow: () => now);
        var item = new MissingPeriodViewModel(store, "trip") { ShouldNotify = true };
        var other = new MissingPeriodViewModel(store, "holiday") { ShouldNotify = true };
        var future = new MissingPeriodViewModel(store, "future") { ShouldNotify = false };
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert(item.CanNotify, "기본 상태는 알림 대상입니다.");
        item.IsNotificationSnoozed = true;
        Assert(!item.CanNotify && other.CanNotify, "체크는 해당 항목만 중지해야 합니다.");
        Assert(changed.Contains(nameof(item.IsNotificationSnoozed)) && changed.Contains(nameof(item.CanNotify)), "바인딩이 변경 통지를 받아야 합니다.");
        now = Start.AddHours(1);
        item.IsNotificationSnoozed = true;
        now = Start.AddHours(2);
        Assert(item.CanNotify, "같은 true 값의 재바인딩이 중지를 연장하면 안 됩니다.");
        Assert(!future.CanNotify, "미래·진행 중 항목은 중지가 없어도 알림을 보내면 안 됩니다.");
    }

    private static AmaranthClient FixtureClient()
    {
        var client = new AmaranthClient();
        DateTime past = DateTime.Today.AddDays(-3);
        client.Schedules.Add(new WorkSchedule { Date = past, Name = "국내출장", FormName = "출장신청서" });
        client.Schedules.Add(new WorkSchedule { Date = past, Name = "휴일근무", FormName = "휴일근무신청서" });
        client.BusinessTripReports.Add(new BusinessTripReport { HolidayWorks = new() { new HolidayWorkEntry { WorkDate = past.AddDays(-1), CompensationType = "대체휴무", StartTime = "08:00", EndTime = "17:00" } } });
        return client;
    }

    private static DashboardViewModel Dashboard(AmaranthClient client, NotificationSnoozeStore store, string user = "employee-1")
    {
        var dashboard = new DashboardViewModel(client, store);
        typeof(DashboardViewModel).GetField("_session", Private)!.SetValue(dashboard, new AmaranthSession { CompanySequence = "company-1", EmployeeSequence = user });
        Apply(dashboard);
        return dashboard;
    }

    private static void Apply(DashboardViewModel dashboard) => typeof(DashboardViewModel).GetMethod("ApplyMissingPeriods", Private)!.Invoke(dashboard, null);

    private static void DashboardReload()
    {
        DateTimeOffset now = Start;
        var store = new NotificationSnoozeStore(utcNow: () => now);
        var dashboard = Dashboard(FixtureClient(), store);
        var original = dashboard.MissingTrips.Single();
        original.IsNotificationSnoozed = true;
        now = Start.AddHours(1);
        Apply(dashboard);
        Assert(!ReferenceEquals(original, dashboard.MissingTrips.Single()), "테스트에서 실제로 카드를 다시 생성해야 합니다.");
        Assert(dashboard.MissingTrips.Single().IsNotificationSnoozed, "카드 재생성 후에도 체크를 유지해야 합니다.");
        Assert(dashboard.MissingTripCount == 1 && !dashboard.IsAllClear, "체크한 미작성 항목을 목록에서 지우면 안 됩니다.");
        now = Start.AddHours(2);
        Apply(dashboard);
        Assert(!dashboard.MissingTrips.Single().IsNotificationSnoozed && dashboard.MissingTrips.Single().CanNotify, "만료 뒤 다음 사이클에 체크가 풀리고 알림 대상이 되어야 합니다.");
    }

    private static void KeyIsolation()
    {
        var store = new NotificationSnoozeStore(utcNow: () => Start);
        var first = Dashboard(FixtureClient(), store);
        first.MissingTrips.Single().IsNotificationSnoozed = true;
        Assert(!first.MissingHolidayWorks.Single().IsNotificationSnoozed, "같은 날짜의 다른 종류가 영향을 받으면 안 됩니다.");
        Assert(!Dashboard(FixtureClient(), store, "employee-2").MissingTrips.Single().IsNotificationSnoozed, "다른 사용자가 영향을 받으면 안 됩니다.");
        var nextDay = FixtureClient();
        nextDay.Schedules[0].Date = nextDay.Schedules[0].Date.AddDays(1);
        Assert(!Dashboard(nextDay, store).MissingTrips.Single().IsNotificationSnoozed, "다른 날짜가 영향을 받으면 안 됩니다.");
        Assert(Dashboard(FixtureClient(), store).MissingTrips.Single().IsNotificationSnoozed, "같은 사용자·종류·날짜는 체크를 유지해야 합니다.");
    }

    private static void NotificationCycles()
    {
        DateTimeOffset now = Start;
        var store = new NotificationSnoozeStore(utcNow: () => now);
        var client = FixtureClient();
        var dashboard = Dashboard(client, store);
        var settings = AppRuntime.Current.Settings;
        var enabled = typeof(AppSettings).GetField("_notificationsEnabled", Private)!;
        object? previousEnabled = enabled.GetValue(settings);
        var previousBalloon = WindowsNotification.TrayBalloon;
        var showGui = typeof(AppSettings).GetField("_showGuiOnNotification", Private)!;
        object? previousShowGui = showGui.GetValue(settings);
        int sent = 0;
        string body = "";
        try
        {
            // 설정 파일·실제 알림·창을 건드리지 않고 집계 결과만 받습니다.
            enabled.SetValue(settings, true);
            showGui.SetValue(settings, false);
            WindowsNotification.TrayBalloon = (_, text) => { sent++; body = text; };
            void cycle() => typeof(DashboardViewModel).GetMethod("NotifyIfNeeded", Private)!.Invoke(dashboard, null);
            cycle();
            Assert(sent == 1 && body.Contains("출장") && body.Contains("휴일근무") && body.Contains("대체휴가"), "세 종류가 알림 집계에 포함되어야 합니다.");
            foreach (var item in dashboard.MissingTrips.Concat(dashboard.MissingHolidayWorks).Concat(dashboard.SubstituteHolidayIssues)) item.IsNotificationSnoozed = true;
            Assert(sent == 1, "체크 조작 자체는 즉시 알림을 보내면 안 됩니다.");
            Apply(dashboard); cycle();
            Assert(sent == 1, "모두 체크한 사이클은 알림이 없어야 합니다.");
            dashboard.MissingTrips.Single().IsNotificationSnoozed = false;
            Assert(sent == 1, "해제는 즉시 알림을 보내면 안 됩니다.");
            cycle();
            Assert(sent == 2 && body.Contains("출장") && !body.Contains("휴일근무") && !body.Contains("대체휴가"), "다음 사이클은 해제된 항목만 알려야 합니다.");
            now = Start.AddHours(2); Apply(dashboard); cycle();
            Assert(sent == 3 && body.Contains("휴일근무") && body.Contains("대체휴가"), "2시간이 지나면 다음 사이클에 다시 포함되어야 합니다.");
            // 시간 불일치 카드도 동일한 중지 정책을 사용합니다.
            client.SubstituteHolidayRequests.Add(new SubstituteHolidayRequest { Month = client.BusinessTripReports[0].HolidayWorks[0].WorkDate.Month,
                Day = client.BusinessTripReports[0].HolidayWorks[0].WorkDate.Day, StartTime = "08:00", EndTime = "16:00" });
            Apply(dashboard);
            Assert(dashboard.SubstituteHolidayIssues.Single().IsTimeMismatch, "시간 오류 픽스처가 필요합니다.");
            dashboard.SubstituteHolidayIssues.Single().IsNotificationSnoozed = true;
            Assert(!dashboard.SubstituteHolidayIssues.Single().CanNotify, "시간 오류 카드도 체크하면 알림을 중지해야 합니다.");
        }
        finally
        {
            enabled.SetValue(settings, previousEnabled);
            showGui.SetValue(settings, previousShowGui);
            WindowsNotification.TrayBalloon = previousBalloon;
        }
    }

    private static void CheckBoxRouting()
    {
        var store = new NotificationSnoozeStore(utcNow: () => Start);
        var item = new MissingPeriodViewModel(store, "card") { ShouldNotify = true };
        var checkBox = new CheckBox { DataContext = item };
        checkBox.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(item.IsNotificationSnoozed)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var card = new Button { Content = checkBox };
        int cardClicks = 0;
        card.Click += (_, _) => cardClicks++;
        var handler = typeof(DashboardPage).GetMethod("NotificationSnooze_Click", Private)!;
        var routedHandler = (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), null, handler);
        checkBox.Click += routedHandler;
        checkBox.IsChecked = true;
        checkBox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, checkBox));
        Assert(item.IsNotificationSnoozed && !item.CanNotify, "WPF TwoWay 바인딩이 체크 값을 모델에 전달해야 합니다.");
        Assert(cardClicks == 0, "체크박스의 Click이 부모 카드에 도달하면 안 됩니다.");
        checkBox.IsChecked = false;
        checkBox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, checkBox));
        Assert(!item.IsNotificationSnoozed && item.CanNotify && cardClicks == 0, "해제도 보고서 버튼을 누르지 않고 반영되어야 합니다.");
        card.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, card));
        Assert(cardClicks == 1, "카드 자체 클릭은 계속 동작해야 합니다.");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (T nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static T Parse<T>(string method, params object[] args) => (T)typeof(AmaranthClient)
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;

    private static void FlexibleDateParsing()
    {
        foreach (string text in new[] { "2026년 9월 20일", "2026 년 09 월 20 일", "2026-9-20", "2026/09/20", "2026.9.20", "20260920" })
        {
            var dates = Parse<List<DateTime>>("ExtractFlexibleDates", text);
            Assert(dates.Count == 1 && dates[0] == new DateTime(2026, 9, 20), "날짜를 정확히 읽어야 합니다: " + text);
        }
        Assert(Parse<List<DateTime>>("ExtractFlexibleDates", "2026년 2월 30일 / 2026-13-1").Count == 0, "유효하지 않은 날짜는 제외해야 합니다.");
        Assert(Parse<List<DateTime>>("ExtractFlexibleDates", "2024년 2월 29일").Single() == new DateTime(2024, 2, 29), "윤년 날짜를 읽어야 합니다.");
    }

    private static void SubstituteDateRegression()
    {
        // 실제 ERP 오탐의 구조를 개인정보 없는 예제로 재현합니다.
        string html = "<table><tr><td>2026년 9월 20일</td><td>08:00 ~ 17:00</td><td>(1)일</td>" +
            "<td><select><option>휴일근무수당</option><option selected>대체휴무</option></select></td><td>2026-09-28</td></tr></table>";
        var entries = Parse<List<HolidayWorkEntry>>("ParseHolidayWorkEntries", html, "휴일근무 2026년 9월 20일 08:00~17:00 합계");
        Assert(entries.Count == 1 && entries[0].WorkDate == new DateTime(2026, 9, 20), "사용예정일 9/28을 새 근무일로 추가하면 안 됩니다.");
        Assert(entries[0].CompensationType == "대체휴무" && entries[0].SubstituteHolidayDateText == "2026-09-28", "근무일과 사용예정일을 분리해서 보관해야 합니다.");
        var client = new AmaranthClient();
        client.BusinessTripReports.Add(new BusinessTripReport { HolidayWorks = entries });
        client.SubstituteHolidayRequests.Add(new SubstituteHolidayRequest { Month = 9, Day = 20, StartTime = "08:00", EndTime = "17:00" });
        Assert(client.GetSubstituteHolidayIssues().Count == 0, "근무일 요청서가 있으면 사용예정일의 미작성 알림이 생기면 안 됩니다.");
        string missingWorkDate = html.Replace("2026년 9월 20일", "년 월 일");
        Assert(Parse<List<HolidayWorkEntry>>("ParseHolidayWorkEntriesFromHtml", missingWorkDate).Count == 0, "근무일이 없으면 사용예정일로 대체하면 안 됩니다.");
    }

    private static void LeaveUsageClassification()
    {
        foreach (string form in new[] { "휴가신청서", "연차휴가신청서" })
        {
            var summary = new ApprovalDocumentSummary { FormId = 40, FormName = form, Title = "[" + form + "] 대체휴가 사용" };
            Assert(!Parse<bool>("IsBusinessTripApplication", summary), "휴가 사용 문서는 출장 대상이 아닙니다.");
            Assert(!Parse<bool>("IsHolidayWorkApplication", summary), "휴가 사용 문서는 휴일근무 대상이 아닙니다.");
            Assert(!Parse<bool>("IsSubstituteHolidayRequest", summary), "휴가 사용 문서는 대체휴가 발생 요청서가 아닙니다.");
        }
    }

    private static void ApplicationDocumentLink()
    {
        var store = new NotificationSnoozeStore(utcNow: () => Start);
        var client = FixtureClient();
        DateTime day = client.Schedules[0].Date;
        var trip = new BusinessTripDocument { DocumentId = 40, FormId = 40, TripStartDate = day, TripEndDate = day, DocContents = "<p>출장 신청서 원문</p>" };
        var holiday = new BusinessTripDocument { DocumentId = 43, FormId = 43, TripStartDate = day, TripEndDate = day, DocContents = "<p>휴일근무 신청서 원문</p>" };
        client.BusinessTripDocuments.Add(trip);
        client.HolidayWorkDocuments.Add(holiday);
        var dashboard = Dashboard(client, store);
        Assert(ReferenceEquals(dashboard.MissingTrips.Single().ApplicationDocument, trip), "출장 카드에 신청서가 연결되어야 합니다.");
        Assert(ReferenceEquals(dashboard.MissingHolidayWorks.Single().ApplicationDocument, holiday), "휴일근무 카드에 신청서가 연결되어야 합니다.");
        Assert(Dashboard(FixtureClient(), store).MissingTrips.Single().ApplicationDocument == null, "신청서가 없을 때 새 문서를 만들어 연결하면 안 됩니다.");
    }

    private static void ApplicationHtml()
    {
        string html = ApplicationDocumentHtml.Create(new BusinessTripDocument { Title = "신청서 <제목>", DocumentNumber = "TEST-1", DocumentStatus = "종결", DocContents = "<html><head><style>td{color:red}</style></head><body><table><tr><td>원문 날짜</td></tr></table></body></html>" });
        Assert(html.Contains("신청서 &lt;제목&gt;") && html.Contains("TEST-1"), "제목은 HTML 이스케이프하고 문서 정보를 표시해야 합니다.");
        Assert(html.Contains("<table><tr><td>원문 날짜</td>") && html.Contains("td{color:red}"), "원문 본문과 양식 스타일을 유지해야 합니다.");
        Assert(html.Contains("Content-Security-Policy") && html.Contains("form-action 'none'"), "원문 보기에서 스크립트·폼 제출을 허용하면 안 됩니다.");
        string fallback = ApplicationDocumentHtml.Create(new BusinessTripDocument { ContentsWord = "텍스트 <원문>" });
        Assert(fallback.Contains("<pre>텍스트 &lt;원문&gt;</pre>"), "HTML이 없으면 텍스트 원문을 표시해야 합니다.");
        Assert(ApplicationDocumentHtml.Create(new BusinessTripDocument()).Contains("신청서 본문이 없습니다"), "본문이 없으면 안내를 표시해야 합니다.");
    }

    private static void DashboardLayout()
    {
        // Application.Run/Window.Show를 호출하지 않으므로 자동 로그인·ERP 호출은 실행되지 않습니다.
        var app = new App();
        app.InitializeComponent();
        var store = new NotificationSnoozeStore(utcNow: () => Start);
        var dashboard = Dashboard(FixtureClient(), store);
        typeof(DashboardViewModel).GetField("_isBusy", Private)!.SetValue(dashboard, false);
        var page = new DashboardPage(new AmaranthSession()) { DataContext = dashboard };
        page.Measure(new Size(886, 753));
        page.Arrange(new Rect(0, 0, 886, 753));
        page.UpdateLayout();
        var checkBoxes = Descendants<CheckBox>(page).ToList();
        Assert(checkBoxes.Count == 3, "출장·휴일근무·대체휴가에 체크박스가 하나씩 있어야 합니다.");
        foreach (CheckBox checkBox in checkBoxes)
        {
            Assert((string)checkBox.Content == "2시간 동안 알리지 않음" && checkBox.ActualWidth > 0 && checkBox.ActualHeight > 0,
                "체크박스 문구와 레이아웃이 표시되어야 합니다.");
            checkBox.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            Assert(((MissingPeriodViewModel)checkBox.DataContext).IsNotificationSnoozed, "실제 XAML 바인딩이 체크를 저장해야 합니다.");
            checkBox.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
        }
        var bitmap = new RenderTargetBitmap(886, 753, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(page);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string path = Path.Combine(AppContext.BaseDirectory, "test-data", "dashboard-preview.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (FileStream stream = File.Create(path)) encoder.Save(stream);
        Console.WriteLine("PREVIEW " + path);
    }

    private static int WebViewSmoke()
    {
        var app = new Application();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        int result = 1;
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            ReportBrowserWindow? window = null;
            try
            {
                string profile = Path.Combine(AppContext.BaseDirectory, "test-data", "webview-" + Guid.NewGuid().ToString("N"));
                var environment = await CoreWebView2Environment.CreateAsync(null, profile);
                typeof(ReportBrowserWindow).GetField("SharedEnvironment", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, environment);
                var document = new BusinessTripDocument { Title = "검증용 출장신청서", DocumentNumber = "TEST-40", DocumentStatus = "종결",
                    DocContents = "<table><tr><td>2026년 9월 20일 원문</td></tr></table><script>window.testInjected=true;</script>" };
                window = new ReportBrowserWindow(new AmaranthSession(), applicationDocument: document)
                {
                    Opacity = 0, ShowActivated = false, ShowInTaskbar = false
                };
                window.Show();
                CoreWebView2 core = await window.InitializeCoreAsync();
                var loaded = new TaskCompletionSource<bool>();
                core.NavigationCompleted += (_, e) =>
                {
                    Console.WriteLine("NAVIGATION success=" + e.IsSuccess + " status=" + e.WebErrorStatus);
                    if (e.IsSuccess) loaded.TrySetResult(true);
                };
                core.NavigateToString(ApplicationDocumentHtml.Create(document));
                if (await Task.WhenAny(loaded.Task, Task.Delay(15000)) != loaded.Task) throw new TimeoutException("WebView2 원문 표시 시간 초과");
                Assert(await loaded.Task, "원문 내비게이션이 성공해야 합니다.");
                string rendered = await core.ExecuteScriptAsync("document.body.innerText.includes('2026년 9월 20일 원문') && !!document.querySelector('table') && window.testInjected !== true");
                Assert(rendered == "true", "실제 WebView2가 원문 표를 표시하고 본문 스크립트를 실행하지 않아야 합니다.");
                Assert(window.Title == "신청서 보기" && (core.Source == "about:blank" || core.Source.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase)), "ERP 작성 화면으로 이동하면 안 됩니다.");
                Assert(typeof(ReportBrowserWindow).GetField("_fill", Private)!.GetValue(window) == null, "신청서 보기에 초안 입력 값이 없어야 합니다.");
                var blocked = new TaskCompletionSource<bool>();
                core.NavigationStarting += (_, e) =>
                {
                    if (e.Uri.StartsWith("https://erp.teia.co.kr/", StringComparison.Ordinal)) blocked.TrySetResult(e.Cancel);
                };
                core.Navigate("https://erp.teia.co.kr/");
                if (await Task.WhenAny(blocked.Task, Task.Delay(5000)) != blocked.Task) throw new TimeoutException("이동 차단 검증 시간 초과");
                Assert(await blocked.Task, "원문에서 ERP 화면 이동이 차단되어야 합니다.");
                Console.WriteLine("PASS 실제 WebView2 신청서 원문·표 표시, 스크립트 및 작성 화면 이동 차단");
                result = 0;
            }
            catch (Exception error)
            {
                Console.WriteLine("FAIL WebView2: " + error.GetBaseException().Message);
            }
            finally
            {
                if (window != null)
                {
                    var browser = (Microsoft.Web.WebView2.Wpf.WebView2)typeof(ReportBrowserWindow).GetField("_browser", Private)!.GetValue(window)!;
                    browser.Dispose();
                }
                window?.Close();
                app.Shutdown(result);
            }
        }));
        return app.Run();
    }
}
