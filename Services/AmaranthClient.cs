using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Amaranth10API.Helpers;
using Amaranth10API.Models;

namespace Amaranth10API.Services;

public sealed class AmaranthClient
{
    private const string BaseUrl = "https://erp.teia.co.kr";
    private const string GroupSeq = "gcmsAmaranth35867";
    private const string LoginPath = "/gw/gw050A02";
    private const string SchedulePath = "/human/attendapplication/at00001";
    private const string ApprovalListPath = "/eap/eap105A04";
    private const string ApprovalDetailPath = "/eap/eap111A04";
    private const int ReportFormId = 208;
    private const string ReportCallComp = "UBAP001";
    private const string TimeValuePattern = @"\d{1,2}\s*[:：.""''‘’“”′″；;．.]?\s*\d{2}";
    private const string TimeRangePattern = TimeValuePattern + @"\s*[~～\-]\s*" + TimeValuePattern;

    private readonly CookieContainer _cookieContainer = new();
    private readonly HttpClient _httpClient;

    public AmaranthClient()
    {
        HttpClientHandler handler = new()
        {
            CookieContainer = _cookieContainer,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        _httpClient = new HttpClient(handler);
        ConfigureHttpClient();
    }

    public List<WorkSchedule> Schedules { get; } = new();
    public List<BusinessTripDocument> BusinessTripDocuments { get; } = new();
    public List<BusinessTripDocument> HolidayWorkDocuments { get; } = new();
    public List<BusinessTripReport> BusinessTripReports { get; } = new();

    public async Task LoadDashboardAsync(
        AmaranthSession session,
        DateTime referenceDate,
        AppSettings settings,
        IProgress<string>? progress = null)
    {
        progress?.Report("출장보고서를 불러오는 중...");
        DateTime reportStart = settings.ReportStart(referenceDate);
        DateTime reportEnd = settings.ReportEnd(referenceDate);
        await LoadBusinessTripReportsAsync(session, reportStart, reportEnd);

        progress?.Report("근태 일정을 불러오는 중...");
        DateTime scheduleStart = reportStart < settings.ApplicationStart(referenceDate)
            ? reportStart
            : settings.ApplicationStart(referenceDate);
        DateTime scheduleEnd = reportEnd > settings.ApplicationEnd(referenceDate)
            ? reportEnd
            : settings.ApplicationEnd(referenceDate);
        await GetScheduleRangeAsync(session, scheduleStart, scheduleEnd);

        progress?.Report("신청서를 불러오는 중...");
        await LoadApplicationDocumentsAsync(
            session,
            settings.ApplicationStart(referenceDate),
            settings.ApplicationEnd(referenceDate));
        progress?.Report("불러오기 완료");
    }

    public async Task<List<BusinessTripReport>> LoadBusinessTripReportsAsync(
        AmaranthSession session,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        if (rangeStart.Date > rangeEnd.Date)
        {
            throw new ArgumentException("조회 시작일이 종료일보다 늦습니다.");
        }

        const int pageSize = 100;
        int page = 1;
        int totalCount = 0;

        BusinessTripReports.Clear();

        do
        {
            ApprovalDocumentPage documentPage = await GetApprovalDocumentPageAsync(
                session,
                rangeStart,
                rangeEnd,
                page,
                pageSize);

            totalCount = documentPage.TotalCount;

            foreach (ApprovalDocumentSummary summary in documentPage.Documents)
            {
                if (ApprovalStatus.IsRejected(summary.DocumentStatus, summary.DocumentStatusCode))
                {
                    continue;
                }

                bool isBusinessTripReport =
                    summary.FormName.Contains("출장&휴일근무보고서", StringComparison.OrdinalIgnoreCase) ||
                    summary.Title.Contains("출장&휴일근무보고서", StringComparison.OrdinalIgnoreCase);

                if (!isBusinessTripReport)
                {
                    continue;
                }

                BusinessTripReport report = await GetBusinessTripReportDetailAsync(session, summary);
                BusinessTripReports.Add(report);
            }

            page++;
        }
        while ((page - 1) * pageSize < totalCount);

        BusinessTripReports.Sort((left, right) =>
        {
            DateTime leftDate = left.TripStartDate ?? left.CreatedDate ?? DateTime.MinValue;
            DateTime rightDate = right.TripStartDate ?? right.CreatedDate ?? DateTime.MinValue;
            return leftDate.CompareTo(rightDate);
        });

        return BusinessTripReports;
    }

    public async Task LoadApplicationDocumentsAsync(
        AmaranthSession session,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }
        if (rangeStart.Date > rangeEnd.Date)
        {
            throw new ArgumentException("조회 시작일이 종료일보다 늦습니다.");
        }

        const int pageSize = 100;
        int page = 1;
        int totalCount = 0;

        BusinessTripDocuments.Clear();
        HolidayWorkDocuments.Clear();

        do
        {
            ApprovalDocumentPage documentPage = await GetApprovalDocumentPageAsync(
                session,
                rangeStart,
                rangeEnd,
                page,
                pageSize);

            totalCount = documentPage.TotalCount;

            foreach (ApprovalDocumentSummary summary in documentPage.Documents)
            {
                if (ApprovalStatus.IsRejected(summary.DocumentStatus, summary.DocumentStatusCode))
                {
                    continue;
                }

                if (IsBusinessTripApplication(summary))
                {
                    BusinessTripDocuments.Add(await GetBusinessTripDocumentDetailAsync(session, summary));
                }
                else if (IsHolidayWorkApplication(summary))
                {
                    HolidayWorkDocuments.Add(await GetHolidayWorkDocumentDetailAsync(session, summary));
                }
            }

            page++;
        }
        while ((page - 1) * pageSize < totalCount);

        BusinessTripDocuments.Sort((left, right) =>
        {
            DateTime leftDate = left.TripStartDate ?? left.CreatedDate ?? DateTime.MinValue;
            DateTime rightDate = right.TripStartDate ?? right.CreatedDate ?? DateTime.MinValue;
            return leftDate.CompareTo(rightDate);
        });
        HolidayWorkDocuments.Sort((left, right) =>
        {
            DateTime leftDate = left.TripStartDate ?? left.CreatedDate ?? DateTime.MinValue;
            DateTime rightDate = right.TripStartDate ?? right.CreatedDate ?? DateTime.MinValue;
            return leftDate.CompareTo(rightDate);
        });
    }

    public async Task<List<BusinessTripDocument>> LoadBusinessTripDocumentsAsync(
        AmaranthSession session,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        await LoadApplicationDocumentsAsync(session, rangeStart, rangeEnd);
        return BusinessTripDocuments;
    }

    private static bool IsBusinessTripApplication(ApprovalDocumentSummary summary)
    {
        if (IsTripHolidayReport(summary))
        {
            return false;
        }

        return summary.FormId == 40 ||
               summary.FormName.Contains("출장신청서", StringComparison.Ordinal);
    }

    private static bool IsHolidayWorkApplication(ApprovalDocumentSummary summary)
    {
        if (IsTripHolidayReport(summary))
        {
            return false;
        }

        return summary.FormId == 43 ||
            summary.FormName.Contains("휴일근무신청서", StringComparison.OrdinalIgnoreCase) ||
            summary.Title.Contains("휴일근무신청서", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTripHolidayReport(ApprovalDocumentSummary summary)
    {
        return summary.FormName.Contains("출장&휴일근무보고서", StringComparison.OrdinalIgnoreCase) ||
               summary.Title.Contains("출장&휴일근무보고서", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCancelledApplication(ApprovalDocumentSummary summary)
    {
        return ContainsCancelMark(summary.Title) || ContainsCancelMark(summary.FormName);
    }

    private static bool ContainsCancelMark(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("취소신청", StringComparison.Ordinal) ||
               value.Contains("상신취소", StringComparison.Ordinal);
    }

    public async Task<AmaranthSession> LoginAsync(string id, string pw)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("아이디를 입력해야 합니다.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(pw))
        {
            throw new ArgumentException("비밀번호를 입력해야 합니다.", nameof(pw));
        }

        await CheckLoginIdAsync(id);

        Dictionary<string, string> loginForm = new()
        {
            ["loginId"] = id,
            ["password"] = pw,
            ["groupSeq"] = GroupSeq,
            ["scLoginYn"] = "N",
            ["fidoPasswdLoginYn"] = "null",
            ["langCode"] = "kr",
            ["isChangeLangCodeYn"] = "N",
            ["a10Domain"] = BaseUrl,
            ["isPlainText"] = "true",
            ["isMobileVerificationIgnore"] = "N"
        };

        using JsonDocument response = await SendLoginApiAsync(LoginPath, loginForm);
        JsonElement root = response.RootElement;
        int resultCode = GetInt32(root, "resultCode");
        string resultMessage = GetString(root, "resultMsg");

        if (resultCode != 200)
        {
            throw new InvalidOperationException($"아마란스 로그인에 실패했습니다. [{resultCode}] {resultMessage}");
        }

        JsonElement sessionInfo = root.GetProperty("resultData").GetProperty("sessionInfo");
        string authToken = GetString(sessionInfo, "auth_a_token");
        string hashKey = GetString(sessionInfo, "hash_key");

        if (string.IsNullOrWhiteSpace(authToken) || string.IsNullOrWhiteSpace(hashKey))
        {
            throw new InvalidOperationException("로그인 응답에서 인증 토큰을 찾지 못했습니다.");
        }

        JsonElement userInfo = sessionInfo.GetProperty("ucUserInfo");

        AmaranthSession session = new()
        {
            AuthToken = authToken,
            HashKey = hashKey,
            SessionTime = GetInt32FromString(sessionInfo, "sessionTime"),
            EmployeeSequence = GetString(userInfo, "empSeq"),
            EmployeeName = GetString(userInfo, "empName"),
            CompanySequence = GetString(userInfo, "compSeq"),
            CompanyName = GetString(userInfo, "compName"),
            DepartmentSequence = GetString(userInfo, "deptSeq"),
            DepartmentName = GetString(userInfo, "deptName"),
            PositionName = GetString(userInfo, "positionName")
        };

        SetAuthenticationCookies(session);
        return session;
    }

    public async Task<List<WorkSchedule>> GetMonthlyScheduleAsync(AmaranthSession session, int year, int month)
    {
        DateTime selectedMonth = new DateTime(year, month, 1);
        return await GetScheduleRangeAsync(
            session,
            selectedMonth.AddMonths(-1),
            selectedMonth.AddMonths(2).AddDays(-1));
    }

    public async Task<List<WorkSchedule>> GetScheduleRangeAsync(
        AmaranthSession session,
        DateTime requestStartDate,
        DateTime requestEndDate)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        var requestData = new
        {
            approStateList = new[] { "0", "1", "4", "5" },
            linkAtCdList = new[]
            {
                "1010", "1020", "1030", "1031", "1040", "1041", "1050", "1060",
                "1070", "1080", "1090", "1110", "2010", "2020", "2030", "3010",
                "4010", "4020", "4030", "4040", "5010", "5011", "5020", "5022",
                "5030", "5031", "5040", "5041", "5021", "5023", "5110", "5120",
                "5121", "8010"
            },
            startDate = requestStartDate.ToString("yyyyMMdd"),
            endDate = requestEndDate.ToString("yyyyMMdd"),
            calendarViewType = "DEFAULT"
        };

        using JsonDocument response = await SendAuthenticatedApiAsync(session, SchedulePath, requestData, "HPD0110");
        JsonElement root = response.RootElement;
        int resultCode = GetInt32(root, "resultCode");
        string resultMessage = GetString(root, "resultMsg");

        if (resultCode != 0)
        {
            throw new InvalidOperationException($"근태일정 조회에 실패했습니다. [{resultCode}] {resultMessage}");
        }

        Schedules.Clear();

        if (!root.TryGetProperty("resultData", out JsonElement resultData) ||
            resultData.ValueKind != JsonValueKind.Array)
        {
            return Schedules;
        }

        foreach (JsonElement item in resultData.EnumerateArray())
        {
            string attendanceDate = GetString(item, "atDt");
            string attendanceName = GetString(item, "atCdNm");

            if (!DateTime.TryParseExact(attendanceDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            {
                continue;
            }

            if (date < requestStartDate || date > requestEndDate)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(attendanceName))
            {
                continue;
            }

            Schedules.Add(new WorkSchedule
            {
                Date = date,
                Name = attendanceName,
                ApprovalState = GetString(item, "approState"),
                FormName = GetString(item, "formNm"),
                StartDate = ParseDate(GetString(item, "startDt")),
                EndDate = ParseDate(GetString(item, "endDt")),
                DocumentId = GetString(item, "idDoc"),
                DocumentNumber = GetString(item, "noDoc"),
                Title = GetString(item, "titleDc"),
                Destination = GetString(item, "arrivNm"),
                BusinessTripLocation = GetString(item, "biztrDc"),
                BusinessTripPurpose = GetString(item, "biztrPpsDc"),
                Task = GetString(item, "taskDc")
            });
        }

        Schedules.Sort((left, right) => left.Date.CompareTo(right.Date));
        return Schedules;
    }

    public List<MissingReportPeriod> GetMissingTripReportPeriods()
    {
        List<WorkSchedule> missingDays = CollectActiveMissingDays(
            BusinessTripDocuments,
            Schedules.Where(schedule => schedule.Name is "해외출장" or "국내출장"),
            "국내출장",
            GuessTripName,
            date => !HasBusinessTripReport(date));

        return GroupMissingPeriods(missingDays, "출장");
    }

    public List<MissingReportPeriod> GetMissingHolidayWorkReportPeriods()
    {
        List<WorkSchedule> missingDays = CollectActiveMissingDays(
            HolidayWorkDocuments,
            Schedules.Where(schedule => schedule.Name == "휴일근무"),
            "휴일근무",
            _ => "휴일근무",
            date => FindHolidayWorkEntry(date) == null);

        return GroupMissingPeriods(missingDays, "휴일근무");
    }

    private List<WorkSchedule> CollectActiveMissingDays(
        IReadOnlyList<BusinessTripDocument> applications,
        IEnumerable<WorkSchedule> calendarDays,
        string defaultName,
        Func<BusinessTripDocument, string> nameForApplication,
        Func<DateTime, bool> isMissingReport)
    {
        Dictionary<DateTime, string> names = new();

        foreach (WorkSchedule schedule in calendarDays)
        {
            if (ContainsCancelMark(schedule.Title) || ContainsCancelMark(schedule.FormName))
            {
                continue;
            }

            DateTime date = schedule.Date.Date;
            names[date] = string.IsNullOrWhiteSpace(schedule.Name) ? defaultName : schedule.Name;
        }

        foreach (BusinessTripDocument document in applications.Where(item =>
                     !item.IsCancellation &&
                     ApprovalStatus.IsClosed(item.DocumentStatus, item.DocumentStatusCode)))
        {
            string name = nameForApplication(document);
            foreach (DateTime date in GetCoveredDates(document))
            {
                if (!names.ContainsKey(date))
                {
                    names[date] = name;
                }
            }
        }

        return names
            .Where(pair => !IsNetCancelledDate(applications, pair.Key) && isMissingReport(pair.Key))
            .Select(pair => new WorkSchedule
            {
                Date = pair.Key,
                Name = pair.Value
            })
            .ToList();
    }

    private static string GuessTripName(BusinessTripDocument document)
    {
        if (document.Title.Contains("해외", StringComparison.Ordinal))
        {
            return "해외출장";
        }

        return "국내출장";
    }

    private bool IsNetCancelledDate(IEnumerable<BusinessTripDocument> documents, DateTime date)
    {
        BusinessTripDocument? latest = documents
            .Where(document => ApprovalStatus.IsClosed(document.DocumentStatus, document.DocumentStatusCode))
            .Where(document => ApplicationCoversDate(document, date))
            .OrderBy(GetDocumentTime)
            .ThenBy(document => document.DocumentId)
            .LastOrDefault();

        return latest is { IsCancellation: true };
    }

    private static DateTime GetDocumentTime(BusinessTripDocument document)
    {
        return document.ActionTime ?? document.CreatedDate ?? DateTime.MinValue;
    }

    private bool ApplicationCoversDate(BusinessTripDocument document, DateTime date)
    {
        return GetCoveredDates(document).Contains(date.Date);
    }

    private IEnumerable<DateTime> GetCoveredDates(BusinessTripDocument document)
    {
        if (document.CoveredDates.Count > 0)
        {
            return document.CoveredDates.Select(date => date.Date).Distinct();
        }

        if (document.WorkDays.Count > 0)
        {
            return document.WorkDays.Select(day => day.WorkDate.Date).Distinct();
        }

        if (!document.TripStartDate.HasValue || !document.TripEndDate.HasValue)
        {
            return Array.Empty<DateTime>();
        }

        DateTime start = document.TripStartDate.Value.Date;
        DateTime end = document.TripEndDate.Value.Date;
        if (end < start)
        {
            return Array.Empty<DateTime>();
        }

        bool holiday = document.FormId == 43 ||
                       document.Title.Contains("휴일근무신청서", StringComparison.Ordinal);
        List<DateTime> calendarDates = Schedules
            .Where(schedule => schedule.Date.Date >= start && schedule.Date.Date <= end)
            .Where(schedule => holiday
                ? schedule.Name == "휴일근무"
                : schedule.Name is "해외출장" or "국내출장")
            .Select(schedule => schedule.Date.Date)
            .Distinct()
            .ToList();
        if (calendarDates.Count > 0)
        {
            return calendarDates;
        }

        List<DateTime> dates = new();
        for (DateTime day = start; day <= end; day = day.AddDays(1))
        {
            dates.Add(day);
        }

        return dates;
    }

    private List<MissingReportPeriod> GroupMissingPeriods(IEnumerable<WorkSchedule> schedules, string kind)
    {
        List<(DateTime Date, string Name)> days = schedules
            .Select(schedule => (schedule.Date.Date, schedule.Name))
            .Distinct()
            .OrderBy(item => item.Date)
            .ThenBy(item => item.Name)
            .ToList();

        List<MissingReportPeriod> periods = new();
        if (days.Count == 0)
        {
            return periods;
        }

        DateTime startDate = days[0].Date;
        DateTime endDate = days[0].Date;
        string name = days[0].Name;

        for (int index = 1; index <= days.Count; index++)
        {
            bool isLast = index == days.Count;
            bool shouldClose = isLast ||
                days[index].Name != name ||
                days[index].Date != endDate.AddDays(1);

            if (!shouldClose)
            {
                endDate = days[index].Date;
                continue;
            }

            periods.Add(new MissingReportPeriod
            {
                Kind = kind,
                Name = name,
                StartDate = startDate,
                EndDate = endDate,
                RelatedApplication = kind == "출장"
                    ? FindOverlappingTripDocument(startDate, endDate)
                    : FindOverlappingHolidayDocument(startDate, endDate)
            });

            if (!isLast)
            {
                startDate = days[index].Date;
                endDate = days[index].Date;
                name = days[index].Name;
            }
        }

        return periods;
    }

    private BusinessTripDocument? FindOverlappingTripDocument(DateTime startDate, DateTime endDate)
    {
        return BusinessTripDocuments
            .Where(document => !document.IsCancellation)
            .Where(document => document.TripStartDate.HasValue && document.TripEndDate.HasValue)
            .Select(document => new
            {
                Document = document,
                Overlap = GetOverlapDays(
                    startDate,
                    endDate,
                    document.TripStartDate!.Value.Date,
                    document.TripEndDate!.Value.Date)
            })
            .Where(item => item.Overlap > 0)
            .OrderByDescending(item => ApprovalStatus.IsClosed(item.Document.DocumentStatus, item.Document.DocumentStatusCode) ? 1 : 0)
            .ThenByDescending(item => item.Overlap)
            .Select(item => item.Document)
            .FirstOrDefault();
    }

    private BusinessTripDocument? FindOverlappingHolidayDocument(DateTime startDate, DateTime endDate)
    {
        return HolidayWorkDocuments
            .Where(document => !document.IsCancellation)
            .Select(document => new
            {
                Document = document,
                Overlap = GetHolidayOverlapDays(document, startDate, endDate)
            })
            .Where(item => item.Overlap > 0)
            .OrderByDescending(item => ApprovalStatus.IsClosed(item.Document.DocumentStatus, item.Document.DocumentStatusCode) ? 1 : 0)
            .ThenByDescending(item => item.Overlap)
            .Select(item => item.Document)
            .FirstOrDefault();
    }

    private static int GetHolidayOverlapDays(BusinessTripDocument document, DateTime startDate, DateTime endDate)
    {
        if (document.WorkDays.Count > 0)
        {
            return document.WorkDays.Count(day =>
                day.WorkDate.Date >= startDate.Date && day.WorkDate.Date <= endDate.Date);
        }

        if (!document.TripStartDate.HasValue || !document.TripEndDate.HasValue)
        {
            return 0;
        }

        return GetOverlapDays(
            startDate,
            endDate,
            document.TripStartDate.Value.Date,
            document.TripEndDate.Value.Date);
    }

    private static int GetOverlapDays(DateTime leftStart, DateTime leftEnd, DateTime rightStart, DateTime rightEnd)
    {
        DateTime start = leftStart > rightStart ? leftStart : rightStart;
        DateTime end = leftEnd < rightEnd ? leftEnd : rightEnd;
        if (end < start)
        {
            return 0;
        }

        return (end - start).Days + 1;
    }

    public static string FormatNullableDate(DateTime? date)
    {
        return date?.ToString("yyyy-MM-dd") ?? "-";
    }

    public static string FormatNullableDateTime(DateTime? date)
    {
        return date?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
    }

    public static string CreateReportDraftUrl()
    {
        return $"{BaseUrl}/#/popup?MicroModuleCode=eap&formId={ReportFormId}&callComp={ReportCallComp}&popupUUID={Guid.NewGuid()}";
    }

    public IReadOnlyList<KeyValuePair<string, string>> GetCookiePairs()
    {
        List<KeyValuePair<string, string>> pairs = new();
        foreach (Cookie cookie in _cookieContainer.GetCookies(new Uri(BaseUrl)))
        {
            pairs.Add(new KeyValuePair<string, string>(cookie.Name, cookie.Value));
        }

        return pairs;
    }

    private async Task<BusinessTripReport> GetBusinessTripReportDetailAsync(
        AmaranthSession session,
        ApprovalDocumentSummary summary)
    {
        var requestData = new
        {
            doc_id = summary.DocumentId.ToString(),
            form_id = summary.FormId.ToString(),
            bindType = "V",
            p_doc_id = 0,
            doc_auth = string.IsNullOrWhiteSpace(summary.DocumentAuth) ? "0" : summary.DocumentAuth,
            spDocId = "",
            setReadYn = "Y",
            commentReqYn = "N",
            pageCode = "UBA1100",
            docToken = ""
        };

        using JsonDocument response = await SendAuthenticatedApiAsync(session, ApprovalDetailPath, requestData, "UBA");
        JsonElement root = response.RootElement;
        int resultCode = GetInt32(root, "resultCode");
        string resultMessage = GetString(root, "resultMsg");

        if (resultCode != 0)
        {
            throw new InvalidOperationException(
                $"출장보고서 상세 조회에 실패했습니다. 문서 ID: {summary.DocumentId}, [{resultCode}] {resultMessage}");
        }

        if (!root.TryGetProperty("resultData", out JsonElement resultData))
        {
            throw new InvalidOperationException($"출장보고서 상세 응답에 resultData가 없습니다. 문서 ID: {summary.DocumentId}");
        }

        string contentsWord = GetString(resultData, "contentsWord");
        string docContents = GetString(resultData, "docContents");
        (DateTime? startDate, DateTime? endDate) = ParseTripPeriodFromReport(contentsWord);
        if (!startDate.HasValue && !endDate.HasValue)
        {
            (startDate, endDate) = ParseTripPeriodFromHtml(docContents);
        }

        if (!startDate.HasValue && !endDate.HasValue)
        {
            (startDate, endDate) = ParseTripPeriodFromReport(ConvertHtmlToText(docContents));
        }

        (startDate, endDate) = AlignTripPeriodToDocumentYear(
            startDate,
            endDate,
            summary.CreatedDate ?? summary.ActionTime);

        List<HolidayWorkEntry> holidayWorks = ParseHolidayWorkEntries(docContents, contentsWord);

        return new BusinessTripReport
        {
            DocumentId = summary.DocumentId,
            FormId = summary.FormId,
            Title = summary.Title,
            DocumentNumber = summary.DocumentNumber,
            DocumentStatus = summary.DocumentStatus,
            DocumentStatusCode = summary.DocumentStatusCode,
            CreatedDate = summary.CreatedDate,
            ActionTime = summary.ActionTime,
            TripStartDate = startDate,
            TripEndDate = endDate,
            ContentsWord = contentsWord,
            DocContents = docContents,
            HolidayWorks = holidayWorks
        };
    }

    private static (DateTime? StartDate, DateTime? EndDate) ParseTripPeriodFromReport(string contentsWord)
    {
        if (string.IsNullOrWhiteSpace(contentsWord))
        {
            return (null, null);
        }

        string normalizedText = NormalizeVisibleText(contentsWord);
        Match periodTitleMatch = Regex.Match(normalizedText, @"출장\s*기간", RegexOptions.IgnoreCase);
        if (!periodTitleMatch.Success)
        {
            return (null, null);
        }

        string periodSection = normalizedText.Substring(periodTitleMatch.Index + periodTitleMatch.Length);
        Match sectionEndMatch = Regex.Match(periodSection, @"출장\s*일수|목적지|휴일근무", RegexOptions.IgnoreCase);
        if (sectionEndMatch.Success)
        {
            periodSection = periodSection.Substring(0, sectionEndMatch.Index);
        }

        Match rangedMatch = Regex.Match(
            periodSection,
            @"(\d{4})\s*년\s*(\d{1,2})\s*월\s*(\d{1,2})\s*일(?:\s*부터)?\s*(\d{4})\s*년\s*(\d{1,2})\s*월\s*(\d{1,2})\s*일(?:\s*까지)?");

        if (rangedMatch.Success)
        {
            DateTime? startDate = TryCreateDate(
                rangedMatch.Groups[1].Value,
                rangedMatch.Groups[2].Value,
                rangedMatch.Groups[3].Value);
            DateTime? endDate = TryCreateDate(
                rangedMatch.Groups[4].Value,
                rangedMatch.Groups[5].Value,
                rangedMatch.Groups[6].Value);

            if (startDate.HasValue && endDate.HasValue)
            {
                return endDate < startDate
                    ? (endDate, startDate)
                    : (startDate, endDate);
            }
        }

        List<DateTime> dates = ExtractFlexibleDates(periodSection);
        if (dates.Count < 2)
        {
            return (null, null);
        }

        DateTime firstDate = dates[0];
        DateTime secondDate = dates[1];
        return secondDate < firstDate
            ? (secondDate, firstDate)
            : (firstDate, secondDate);
    }

    private static (DateTime? StartDate, DateTime? EndDate) ParseTripPeriodFromHtml(string docContents)
    {
        if (string.IsNullOrWhiteSpace(docContents))
        {
            return (null, null);
        }

        Match rowMatch = Regex.Match(
            docContents,
            @"<tr\b[^>]*>(?:(?!</tr>).)*출장\s*기간(?:(?!</tr>).)*</tr>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!rowMatch.Success)
        {
            return (null, null);
        }

        List<int> selectedNumbers = new();
        foreach (Match option in Regex.Matches(
            rowMatch.Value,
            @"<option\b[^>]*\bselected(?:\s*=\s*[""'][^""']*[""'])?[^>]*>(?<text>.*?)</option>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string digits = Regex.Replace(ConvertHtmlToText(option.Groups["text"].Value), @"\D", string.Empty);
            if (int.TryParse(digits, out int number))
            {
                selectedNumbers.Add(number);
            }
        }

        if (selectedNumbers.Count < 6)
        {
            return (null, null);
        }

        DateTime? startDate = TryCreateDate(
            selectedNumbers[0].ToString(),
            selectedNumbers[1].ToString(),
            selectedNumbers[2].ToString());
        DateTime? endDate = TryCreateDate(
            selectedNumbers[3].ToString(),
            selectedNumbers[4].ToString(),
            selectedNumbers[5].ToString());

        if (!startDate.HasValue || !endDate.HasValue)
        {
            return (null, null);
        }

        return endDate < startDate
            ? (endDate, startDate)
            : (startDate, endDate);
    }

    private static (DateTime? StartDate, DateTime? EndDate) AlignTripPeriodToDocumentYear(
        DateTime? startDate,
        DateTime? endDate,
        DateTime? documentDate)
    {
        if (!startDate.HasValue || !endDate.HasValue || !documentDate.HasValue)
        {
            return (startDate, endDate);
        }

        DateTime start = startDate.Value.Date;
        DateTime end = endDate.Value.Date;
        DateTime document = documentDate.Value.Date;
        if (start.Year == document.Year || end.Year == document.Year)
        {
            return (start, end);
        }

        int yearDelta = document.Year - end.Year;
        DateTime shiftedStart = start.AddYears(yearDelta);
        DateTime shiftedEnd = end.AddYears(yearDelta);
        if (shiftedEnd <= document.AddDays(14) && shiftedStart >= document.AddMonths(-8))
        {
            return (shiftedStart, shiftedEnd);
        }

        return (start, end);
    }

    private static List<HolidayWorkEntry> ParseHolidayWorkEntries(string docContents, string contentsWord)
    {
        Dictionary<DateTime, HolidayWorkEntry> holidayWorks = new();

        foreach (HolidayWorkEntry entry in ParseHolidayWorkEntriesFromHtml(docContents))
        {
            holidayWorks[entry.WorkDate.Date] = entry;
        }

        foreach (HolidayWorkEntry entry in ParseHolidayWorkEntriesFromText(contentsWord))
        {
            if (!holidayWorks.ContainsKey(entry.WorkDate.Date))
            {
                holidayWorks.Add(entry.WorkDate.Date, entry);
            }
        }

        return holidayWorks.Values
            .OrderBy(entry => entry.WorkDate)
            .ToList();
    }

    private static List<HolidayWorkEntry> ParseHolidayWorkEntriesFromText(string contentsWord)
    {
        List<HolidayWorkEntry> holidayWorks = new();
        if (string.IsNullOrWhiteSpace(contentsWord))
        {
            return holidayWorks;
        }

        string normalizedText = NormalizeVisibleText(contentsWord);
        Match holidayTitleMatch = Regex.Match(normalizedText, @"휴일근무");
        if (!holidayTitleMatch.Success)
        {
            return holidayWorks;
        }

        string holidaySection = normalizedText.Substring(holidayTitleMatch.Index);
        Match sectionEndMatch = Regex.Match(holidaySection, @"합\s*계|업무내용|아래와 같이");
        if (sectionEndMatch.Success)
        {
            holidaySection = holidaySection.Substring(0, sectionEndMatch.Index);
        }

        MatchCollection matches = Regex.Matches(
            holidaySection,
            $@"(\d{{4}})\s*년\s*(\d{{1,2}})\s*월\s*(\d{{1,2}})\s*일\s+{TimeRangePattern}");

        foreach (Match match in matches)
        {
            DateTime? workDate = TryCreateDate(
                match.Groups[1].Value,
                match.Groups[2].Value,
                match.Groups[3].Value);

            if (!workDate.HasValue)
            {
                continue;
            }

            holidayWorks.Add(new HolidayWorkEntry
            {
                WorkDate = workDate.Value.Date,
                CompensationType = "휴일근무수당"
            });
        }

        return holidayWorks;
    }

    private static List<HolidayWorkEntry> ParseHolidayWorkEntriesFromHtml(string docContents)
    {
        List<HolidayWorkEntry> holidayWorks = new();
        if (string.IsNullOrWhiteSpace(docContents))
        {
            return holidayWorks;
        }

        MatchCollection rowMatches = Regex.Matches(
            docContents,
            @"<tr\b[^>]*>(?<row>.*?)</tr>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match rowMatch in rowMatches)
        {
            string rowHtml = rowMatch.Groups["row"].Value;
            string rowText = ConvertHtmlToText(rowHtml);

            if (!Regex.IsMatch(rowText, TimeRangePattern))
            {
                continue;
            }

            List<DateTime> rowDates = ExtractFlexibleDates(rowText);
            if (rowDates.Count == 0)
            {
                continue;
            }

            string compensationType = ExtractSelectedOptionText(rowHtml);
            if (compensationType is not ("휴일근무수당" or "대체휴무"))
            {
                compensationType = "휴일근무수당";
            }

            List<string> cellValues = ExtractTableCellValues(rowHtml);
            int compensationCellIndex = FindCompensationCellIndex(rowHtml);
            string substituteHolidayDateText = string.Empty;

            if (compensationCellIndex >= 0 && compensationCellIndex + 1 < cellValues.Count)
            {
                substituteHolidayDateText = cellValues[compensationCellIndex + 1];
            }

            holidayWorks.Add(new HolidayWorkEntry
            {
                WorkDate = rowDates[0].Date,
                CompensationType = compensationType,
                SubstituteHolidayDateText = substituteHolidayDateText
            });
        }

        return holidayWorks
            .GroupBy(entry => entry.WorkDate.Date)
            .Select(group => group.First())
            .ToList();
    }

    private static DateTime? TryCreateDate(string yearText, string monthText, string dayText)
    {
        if (!int.TryParse(yearText, out int year) ||
            !int.TryParse(monthText, out int month) ||
            !int.TryParse(dayText, out int day))
        {
            return null;
        }

        try
        {
            return new DateTime(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static List<DateTime> ExtractFlexibleDates(string text)
    {
        List<DateTime> dates = new();
        if (string.IsNullOrWhiteSpace(text))
        {
            return dates;
        }

        const string datePattern =
            @"\d{4}\s*(?:년\s*|[-./]\s*)" +
            @"\d{1,2}\s*(?:월\s*|[-./]\s*)" +
            @"\d{1,2}\s*일?|\b\d{8}\b";

        foreach (Match match in Regex.Matches(text, datePattern))
        {
            string digitsOnly = Regex.Replace(match.Value, @"\D", string.Empty);
            if (digitsOnly.Length != 8)
            {
                continue;
            }

            if (DateTime.TryParseExact(digitsOnly, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            {
                dates.Add(date);
            }
        }

        return dates;
    }

    private static string ExtractSelectedOptionText(string html)
    {
        Match selectedOptionMatch = Regex.Match(
            html,
            @"<option\b[^>]*\bselected(?:\s*=\s*[""'][^""']*[""'])?[^>]*>(?<text>.*?)</option>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return selectedOptionMatch.Success
            ? ConvertHtmlToText(selectedOptionMatch.Groups["text"].Value)
            : string.Empty;
    }

    private static List<string> ExtractTableCellValues(string rowHtml)
    {
        List<string> values = new();
        MatchCollection cellMatches = Regex.Matches(
            rowHtml,
            @"<td\b[^>]*>(?<cell>.*?)</td>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match cellMatch in cellMatches)
        {
            string cellHtml = cellMatch.Groups["cell"].Value;
            string cellText = ConvertHtmlToText(cellHtml);
            if (string.IsNullOrWhiteSpace(cellText))
            {
                cellText = ExtractInputValue(cellHtml);
            }

            values.Add(cellText);
        }

        return values;
    }

    private static int FindCompensationCellIndex(string rowHtml)
    {
        MatchCollection cellMatches = Regex.Matches(
            rowHtml,
            @"<td\b[^>]*>(?<cell>.*?)</td>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        for (int index = 0; index < cellMatches.Count; index++)
        {
            string selectedOption = ExtractSelectedOptionText(cellMatches[index].Groups["cell"].Value);
            if (selectedOption is "휴일근무수당" or "대체휴무")
            {
                return index;
            }
        }

        return -1;
    }

    private static string ExtractInputValue(string html)
    {
        Match inputMatch = Regex.Match(
            html,
            @"<input\b[^>]*\bvalue\s*=\s*[""'](?<value>[^""']*)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return inputMatch.Success
            ? NormalizeVisibleText(WebUtility.HtmlDecode(inputMatch.Groups["value"].Value))
            : string.Empty;
    }

    private static string ConvertHtmlToText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        string text = Regex.Replace(html, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", " ", RegexOptions.Singleline);
        text = WebUtility.HtmlDecode(text);
        return NormalizeVisibleText(text);
    }

    private static string NormalizeVisibleText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(
            text.Replace('\u00A0', ' ').Replace('\u3000', ' '),
            @"\s+",
            " ").Trim();
    }

    private HolidayWorkEntry? FindHolidayWorkEntry(DateTime scheduleDate)
    {
        return BusinessTripReports
            .Where(report => !ApprovalStatus.IsRejected(report.DocumentStatus, report.DocumentStatusCode))
            .OrderByDescending(report => report.ActionTime ?? report.CreatedDate ?? DateTime.MinValue)
            .SelectMany(report => report.HolidayWorks)
            .FirstOrDefault(entry => entry.WorkDate.Date == scheduleDate.Date);
    }

    private bool HasBusinessTripReport(DateTime scheduleDate)
    {
        foreach (BusinessTripReport report in BusinessTripReports)
        {
            if (ApprovalStatus.IsRejected(report.DocumentStatus, report.DocumentStatusCode))
            {
                continue;
            }

            if (ReportCoversDate(report, scheduleDate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReportCoversDate(BusinessTripReport report, DateTime scheduleDate)
    {
        if (!report.TripStartDate.HasValue || !report.TripEndDate.HasValue)
        {
            return false;
        }

        DateTime startDate = report.TripStartDate.Value.Date;
        DateTime endDate = report.TripEndDate.Value.Date;
        DateTime date = scheduleDate.Date;
        if (date >= startDate && date <= endDate)
        {
            return true;
        }

        DateTime? documentDate = report.CreatedDate ?? report.ActionTime;
        if (!documentDate.HasValue || documentDate.Value.Year != date.Year)
        {
            return false;
        }

        if (startDate.Year == date.Year || endDate.Year == date.Year)
        {
            return false;
        }

        int yearDelta = date.Year - endDate.Year;
        DateTime shiftedStart = startDate.AddYears(yearDelta);
        DateTime shiftedEnd = endDate.AddYears(yearDelta);
        return date >= shiftedStart && date <= shiftedEnd;
    }

    private async Task<ApprovalDocumentPage> GetApprovalDocumentPageAsync(
        AmaranthSession session,
        DateTime rangeStart,
        DateTime rangeEnd,
        int page,
        int pageSize)
    {
        var requestData = new
        {
            fDocSts = Array.Empty<string>(),
            page = page.ToString(),
            pageSize = pageSize.ToString(),
            eaBoxId = "1000900",
            nMenuID = "1001100",
            sfrDt = rangeStart.ToString("yyyyMMdd"),
            stoDt = rangeEnd.ToString("yyyyMMdd"),
            sFormId = new[] { "0" },
            periodPicker = "ACTION_TIME",
            sortField = "ACTION_TIME",
            sortType = "DESC",
            docContentsData = new { },
            item = new { },
            useElasticSearch = true,
            useElasticSearch_new = true,
            pageCode = "UBA2030",
            upperMenuNo = "1000900",
            menuNo = "1001100"
        };

        using JsonDocument response = await SendAuthenticatedApiAsync(session, ApprovalListPath, requestData, "UBA");
        JsonElement root = response.RootElement;
        int resultCode = GetInt32(root, "resultCode");
        string resultMessage = GetString(root, "resultMsg");

        if (resultCode != 0)
        {
            throw new InvalidOperationException($"결재문서 목록 조회에 실패했습니다. [{resultCode}] {resultMessage}");
        }

        if (!root.TryGetProperty("resultData", out JsonElement resultData) ||
            !resultData.TryGetProperty("map", out JsonElement map))
        {
            return new ApprovalDocumentPage();
        }

        ApprovalDocumentPage result = new()
        {
            TotalCount = GetInt32(map, "totalCount")
        };

        if (!map.TryGetProperty("list", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement item in list.EnumerateArray())
        {
            result.Documents.Add(new ApprovalDocumentSummary
            {
                DocumentId = GetInt32(item, "DOC_ID"),
                FormId = GetInt32(item, "FORM_ID"),
                FormName = GetString(item, "FORM_NM"),
                Title = GetString(item, "DOC_TITLE"),
                DocumentNumber = GetString(item, "DOC_NO"),
                DocumentStatus = GetString(item, "DOC_STSNM"),
                DocumentStatusCode = GetStatusCode(item),
                DocumentAuth = GetString(item, "DOC_SECURITY"),
                CreatedDate = ParseDateTime(GetString(item, "CREATED_DT")),
                ActionTime = ParseDateTime(GetString(item, "ACTION_TIME"))
            });
        }

        return result;
    }

    private async Task<(string ContentsWord, string DocContents)> GetApprovalDetailContentsAsync(
        AmaranthSession session,
        ApprovalDocumentSummary summary,
        string failureLabel)
    {
        var requestData = new
        {
            doc_id = summary.DocumentId.ToString(),
            form_id = summary.FormId.ToString(),
            bindType = "V",
            p_doc_id = 0,
            doc_auth = string.IsNullOrWhiteSpace(summary.DocumentAuth) ? "0" : summary.DocumentAuth,
            spDocId = "",
            setReadYn = "Y",
            commentReqYn = "N",
            pageCode = "UBA1100",
            docToken = ""
        };

        using JsonDocument response = await SendAuthenticatedApiAsync(session, ApprovalDetailPath, requestData, "UBA");
        JsonElement root = response.RootElement;
        int resultCode = GetInt32(root, "resultCode");
        string resultMessage = GetString(root, "resultMsg");

        if (resultCode != 0)
        {
            throw new InvalidOperationException(
                $"{failureLabel} 상세 조회에 실패했습니다. 문서 ID: {summary.DocumentId}, [{resultCode}] {resultMessage}");
        }

        if (!root.TryGetProperty("resultData", out JsonElement resultData))
        {
            throw new InvalidOperationException($"{failureLabel} 상세 응답에 resultData가 없습니다. 문서 ID: {summary.DocumentId}");
        }

        string contentsWord = GetString(resultData, "contentsWord");
        string docContents = GetString(resultData, "docContents");
        if (string.IsNullOrWhiteSpace(docContents))
        {
            docContents = GetString(resultData, "contents");
        }

        return (contentsWord, docContents);
    }

    private async Task<BusinessTripDocument> GetBusinessTripDocumentDetailAsync(
        AmaranthSession session,
        ApprovalDocumentSummary summary)
    {
        (string contentsWord, string docContents) =
            await GetApprovalDetailContentsAsync(session, summary, "출장신청서");

        (DateTime? tripStartDate, DateTime? tripEndDate, string startTime, string endTime) =
            ParseBusinessTripPeriodAndTimes(contentsWord);
        if (!tripStartDate.HasValue && !tripEndDate.HasValue)
        {
            (tripStartDate, tripEndDate, startTime, endTime) =
                ParseBusinessTripPeriodAndTimes(ConvertHtmlToText(docContents));
        }

        return CreateApplicationDocument(summary, contentsWord, docContents, tripStartDate, tripEndDate, startTime, endTime);
    }

    private async Task<BusinessTripDocument> GetHolidayWorkDocumentDetailAsync(
        AmaranthSession session,
        ApprovalDocumentSummary summary)
    {
        (string contentsWord, string docContents) =
            await GetApprovalDetailContentsAsync(session, summary, "휴일근무신청서");

        List<HolidayWorkDay> workDays = ParseHolidayWorkScheduleDays(contentsWord, docContents);
        DateTime? startDate = workDays.Count > 0 ? workDays.Min(day => day.WorkDate.Date) : null;
        DateTime? endDate = workDays.Count > 0 ? workDays.Max(day => day.WorkDate.Date) : null;
        string startTime = workDays.Count > 0 ? workDays[0].StartTime : string.Empty;
        string endTime = workDays.Count > 0 ? workDays[0].EndTime : string.Empty;

        BusinessTripDocument document = CreateApplicationDocument(
            summary,
            contentsWord,
            docContents,
            startDate,
            endDate,
            startTime,
            endTime);
        document.WorkDays = workDays;
        return document;
    }

    private static BusinessTripDocument CreateApplicationDocument(
        ApprovalDocumentSummary summary,
        string contentsWord,
        string docContents,
        DateTime? tripStartDate,
        DateTime? tripEndDate,
        string startTime,
        string endTime)
    {
        List<(DateTime Start, DateTime End)> titleRanges =
            ParseTitleDateRanges(summary.Title, summary.CreatedDate ?? summary.ActionTime);
        List<DateTime> coveredDates = ResolveCoveredDates(titleRanges, contentsWord, docContents);

        if (coveredDates.Count > 0)
        {
            tripStartDate ??= coveredDates[0];
            tripEndDate ??= coveredDates[coveredDates.Count - 1];
        }
        else if (titleRanges.Count > 0)
        {
            tripStartDate ??= titleRanges.Min(range => range.Start);
            tripEndDate ??= titleRanges.Max(range => range.End);
        }

        return new BusinessTripDocument
        {
            DocumentId = summary.DocumentId,
            FormId = summary.FormId,
            Title = summary.Title,
            DocumentNumber = summary.DocumentNumber,
            DocumentStatus = summary.DocumentStatus,
            DocumentStatusCode = summary.DocumentStatusCode,
            CreatedDate = summary.CreatedDate,
            ActionTime = summary.ActionTime,
            TripStartDate = tripStartDate,
            TripEndDate = tripEndDate,
            StartTime = startTime,
            EndTime = endTime,
            ContentsWord = contentsWord,
            DocContents = docContents,
            CoveredDates = coveredDates,
            IsCancellation = IsCancelledApplication(summary)
        };
    }

    private static List<DateTime> ResolveCoveredDates(
        List<(DateTime Start, DateTime End)> titleRanges,
        string contentsWord,
        string docContents)
    {
        if (titleRanges.Count >= 2)
        {
            return ExpandDateRanges(titleRanges);
        }

        List<(DateTime Start, DateTime End)> bodyRanges = new();
        bodyRanges.AddRange(ParseFullDateRanges(contentsWord));
        bodyRanges.AddRange(ParseFullDateRanges(ConvertHtmlToText(docContents)));
        if (bodyRanges.Count >= 2)
        {
            return ExpandDateRanges(bodyRanges);
        }

        return new List<DateTime>();
    }

    private static List<(DateTime Start, DateTime End)> ParseTitleDateRanges(string title, DateTime? yearHint)
    {
        List<(DateTime Start, DateTime End)> ranges = new();
        if (string.IsNullOrWhiteSpace(title))
        {
            return ranges;
        }

        int year = (yearHint ?? DateTime.Today).Year;
        foreach (Match match in Regex.Matches(title, @"(\d{1,2})-(\d{1,2})\s*[~\-]\s*(\d{1,2})-(\d{1,2})"))
        {
            if (!int.TryParse(match.Groups[1].Value, out int startMonth) ||
                !int.TryParse(match.Groups[2].Value, out int startDay) ||
                !int.TryParse(match.Groups[3].Value, out int endMonth) ||
                !int.TryParse(match.Groups[4].Value, out int endDay))
            {
                continue;
            }

            try
            {
                DateTime start = new(year, startMonth, startDay);
                DateTime end = new(year, endMonth, endDay);
                if (end >= start)
                {
                    ranges.Add((start, end));
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // 제목의 날짜가 잘못된 경우 해당 구간만 건너뜁니다.
            }
        }

        return ranges;
    }

    private static List<(DateTime Start, DateTime End)> ParseFullDateRanges(string text)
    {
        List<(DateTime Start, DateTime End)> ranges = new();
        if (string.IsNullOrWhiteSpace(text))
        {
            return ranges;
        }

        string normalized = NormalizeVisibleText(text);
        foreach (Match match in Regex.Matches(normalized, @"(\d{4}-\d{2}-\d{2})\s*~\s*(\d{4}-\d{2}-\d{2})"))
        {
            DateTime? start = ParseHyphenDate(match.Groups[1].Value);
            DateTime? end = ParseHyphenDate(match.Groups[2].Value);
            if (start.HasValue && end.HasValue && end.Value >= start.Value)
            {
                ranges.Add((start.Value.Date, end.Value.Date));
            }
        }

        return ranges;
    }

    private static List<DateTime> ExpandDateRanges(IEnumerable<(DateTime Start, DateTime End)> ranges)
    {
        SortedSet<DateTime> dates = new();
        foreach ((DateTime start, DateTime end) in ranges)
        {
            for (DateTime day = start.Date; day <= end.Date; day = day.AddDays(1))
            {
                dates.Add(day);
            }
        }

        return dates.ToList();
    }

    private static (DateTime? StartDate, DateTime? EndDate, string StartTime, string EndTime) ParseBusinessTripPeriodAndTimes(
        string contentsWord)
    {
        if (string.IsNullOrWhiteSpace(contentsWord))
        {
            return (null, null, string.Empty, string.Empty);
        }

        string normalized = NormalizeVisibleText(contentsWord);
        const string pattern =
            @"출장기간\s*(\d{4}-\d{2}-\d{2})(?:\([^)]+\))?(?:\s+(\d{1,2}:\d{2}))?\s*~\s*(\d{4}-\d{2}-\d{2})(?:\([^)]+\))?(?:\s+(\d{1,2}:\d{2}))?";

        Match match = Regex.Match(normalized, pattern);
        DateTime? startDate = null;
        DateTime? endDate = null;
        string startTime = string.Empty;
        string endTime = string.Empty;

        if (match.Success)
        {
            startDate = ParseHyphenDate(match.Groups[1].Value);
            startTime = NormalizeClock(match.Groups[2].Value);
            endDate = ParseHyphenDate(match.Groups[3].Value);
            endTime = NormalizeClock(match.Groups[4].Value);
        }

        if (string.IsNullOrWhiteSpace(startTime) || string.IsNullOrWhiteSpace(endTime))
        {
            Match timeMatch = Regex.Match(
                normalized,
                $@"(?:시간|출장\s*시간)\s*({TimeValuePattern})\s*[~～\-]\s*({TimeValuePattern})");
            if (timeMatch.Success)
            {
                if (string.IsNullOrWhiteSpace(startTime))
                {
                    startTime = NormalizeClock(timeMatch.Groups[1].Value);
                }

                if (string.IsNullOrWhiteSpace(endTime))
                {
                    endTime = NormalizeClock(timeMatch.Groups[2].Value);
                }
            }
        }

        return (startDate, endDate, startTime, endTime);
    }

    private static List<HolidayWorkDay> ParseHolidayWorkScheduleDays(string contentsWord, string docContents)
    {
        Dictionary<DateTime, HolidayWorkDay> days = new();
        foreach (string text in new[] { contentsWord, ConvertHtmlToText(docContents) })
        {
            foreach (HolidayWorkDay day in ParseHolidayWorkScheduleDaysFromText(text))
            {
                days[day.WorkDate.Date] = day;
            }
        }

        return days.Values.OrderBy(day => day.WorkDate).ToList();
    }

    private static List<HolidayWorkDay> ParseHolidayWorkScheduleDaysFromText(string text)
    {
        List<HolidayWorkDay> days = new();
        if (string.IsNullOrWhiteSpace(text))
        {
            return days;
        }

        string normalized = NormalizeVisibleText(text);
        string pattern =
            @"(\d{4}-\d{2}-\d{2})(?:\s*\([^)]+\))?(?:\s+[가-힣]{1,3})?\s+(" +
            TimeValuePattern +
            @")\s*[~～\-]\s*(" +
            TimeValuePattern +
            @")";

        foreach (Match match in Regex.Matches(normalized, pattern))
        {
            DateTime? workDate = ParseHyphenDate(match.Groups[1].Value);
            string startTime = NormalizeClock(match.Groups[2].Value);
            string endTime = NormalizeClock(match.Groups[3].Value);
            if (!workDate.HasValue || string.IsNullOrWhiteSpace(startTime) || string.IsNullOrWhiteSpace(endTime))
            {
                continue;
            }

            days.Add(new HolidayWorkDay
            {
                WorkDate = workDate.Value.Date,
                StartTime = startTime,
                EndTime = endTime
            });
        }

        return days;
    }

    private static string NormalizeClock(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Match match = Regex.Match(value, @"(\d{1,2})\D+(\d{2})");
        if (!match.Success)
        {
            return string.Empty;
        }

        if (!int.TryParse(match.Groups[1].Value, out int hour) ||
            !int.TryParse(match.Groups[2].Value, out int minute))
        {
            return string.Empty;
        }

        if (hour is < 0 or > 23 || minute is < 0 or > 59)
        {
            return string.Empty;
        }

        return $"{hour:00}:{minute:00}";
    }

    private static (DateTime? StartDate, DateTime? EndDate) ParseBusinessTripPeriod(string contentsWord)
    {
        (DateTime? startDate, DateTime? endDate, _, _) = ParseBusinessTripPeriodAndTimes(contentsWord);
        return (startDate, endDate);
    }

    private static DateTime? ParseHyphenDate(string value)
    {
        return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
            ? date
            : null;
    }

    private static DateTime? ParseDateTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] formats =
        {
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.fffK",
            "yyyy-MM-dd"
        };

        return DateTime.TryParseExact(
            value,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out DateTime date)
            ? date
            : null;
    }

    private async Task<JsonDocument> SendAuthenticatedApiAsync(
        AmaranthSession session,
        string requestPath,
        object requestData,
        string menuCode)
    {
        string transactionId = Guid.NewGuid().ToString("N");
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature = CreateApiSignature(session.AuthToken, session.HashKey, transactionId, timestamp, requestPath);
        string json = JsonSerializer.Serialize(requestData);

        using HttpRequestMessage request = new(HttpMethod.Post, BaseUrl + requestPath);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {session.AuthToken}");
        request.Headers.TryAddWithoutValidation("Origin", BaseUrl);
        request.Headers.TryAddWithoutValidation("Referer", BaseUrl + "/");
        request.Headers.TryAddWithoutValidation("Timestamp", timestamp.ToString());
        request.Headers.TryAddWithoutValidation("Transaction-Id", transactionId);
        request.Headers.TryAddWithoutValidation("Wehago-Sign", signature);
        request.Headers.TryAddWithoutValidation("Menu-Code", menuCode);
        request.Headers.TryAddWithoutValidation("Use-Multilang", "false");

        using HttpResponseMessage response = await _httpClient.SendAsync(request);
        string responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"근태일정 API 호출에 실패했습니다. HTTP {(int)response.StatusCode}: {responseText}");
        }

        try
        {
            return JsonDocument.Parse(responseText);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"근태일정 응답이 올바른 JSON이 아닙니다: {responseText}", exception);
        }
    }

    private async Task<JsonDocument> SendLoginApiAsync(string requestPath, Dictionary<string, string> form)
    {
        UnsignedToken token = await GetUnsignedTokenAsync(requestPath);
        string signature = CreateLoginSignature(token.Token, token.CurrentDate, token.TransactionId, requestPath);
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using HttpRequestMessage request = new(HttpMethod.Post, BaseUrl + requestPath);
        request.Content = new FormUrlEncodedContent(form);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer undefined");
        request.Headers.TryAddWithoutValidation("Origin", BaseUrl);
        request.Headers.TryAddWithoutValidation("Referer", BaseUrl + "/");
        request.Headers.TryAddWithoutValidation("Signature", signature);
        request.Headers.TryAddWithoutValidation("Timestamp", timestamp.ToString());
        request.Headers.TryAddWithoutValidation("Transaction-Id", token.TransactionId);

        using HttpResponseMessage response = await _httpClient.SendAsync(request);
        string responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"로그인 API 호출에 실패했습니다. HTTP {(int)response.StatusCode}: {responseText}");
        }

        try
        {
            return JsonDocument.Parse(responseText);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"로그인 응답이 올바른 JSON이 아닙니다: {responseText}", exception);
        }
    }

    private async Task CheckLoginIdAsync(string id)
    {
        string encodedLoginId = Convert.ToBase64String(Encoding.UTF8.GetBytes(id));
        Dictionary<string, string> form = new()
        {
            ["loginId"] = encodedLoginId,
            ["groupSeq"] = GroupSeq,
            ["loginType"] = "checkLoginId",
            ["apiTarget"] = "web",
            ["langCode"] = "kr",
            ["a10Domain"] = BaseUrl
        };

        using JsonDocument response = await SendLoginApiAsync(LoginPath, form);
        int resultCode = GetInt32(response.RootElement, "resultCode");
        string resultMessage = GetString(response.RootElement, "resultMsg");

        if (resultCode != 200)
        {
            throw new InvalidOperationException($"아이디 확인에 실패했습니다. [{resultCode}] {resultMessage}");
        }
    }

    private async Task<UnsignedToken> GetUnsignedTokenAsync(string requestPath)
    {
        string transactionId = Guid.NewGuid().ToString("N");
        long cacheBuster = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string requestUrl = $"{BaseUrl}/get_token/?url={Uri.EscapeDataString(requestPath)}&_={cacheBuster}";

        using HttpRequestMessage request = new(HttpMethod.Get, requestUrl);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");
        request.Headers.TryAddWithoutValidation("Referer", BaseUrl + "/");
        request.Headers.TryAddWithoutValidation("Transaction-Id", transactionId);

        using HttpResponseMessage response = await _httpClient.SendAsync(request);
        string responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"로그인 토큰 발급에 실패했습니다. HTTP {(int)response.StatusCode}: {responseText}");
        }

        TokenResponse? tokenResponse;
        try
        {
            tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseText);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"토큰 응답이 올바른 JSON이 아닙니다: {responseText}", exception);
        }

        if (tokenResponse == null ||
            string.IsNullOrWhiteSpace(tokenResponse.Token) ||
            string.IsNullOrWhiteSpace(tokenResponse.CurrentDate))
        {
            throw new InvalidOperationException($"토큰 응답에 필요한 값이 없습니다: {responseText}");
        }

        return new UnsignedToken
        {
            Token = tokenResponse.Token,
            CurrentDate = tokenResponse.CurrentDate,
            TransactionId = transactionId
        };
    }

    private static string CreateLoginSignature(string token, string currentDate, string transactionId, string requestPath)
    {
        string source = token + currentDate + transactionId + requestPath;
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(source));
        return Convert.ToBase64String(hash);
    }

    private static string CreateApiSignature(
        string authToken,
        string hashKey,
        string transactionId,
        long timestamp,
        string requestPath)
    {
        string source = authToken + transactionId + timestamp + requestPath;
        byte[] keyBytes = Encoding.UTF8.GetBytes(hashKey);
        byte[] sourceBytes = Encoding.UTF8.GetBytes(source);

        using HMACSHA256 hmac = new(keyBytes);
        byte[] hash = hmac.ComputeHash(sourceBytes);
        return Convert.ToBase64String(hash);
    }

    private void SetAuthenticationCookies(AmaranthSession session)
    {
        Uri uri = new(BaseUrl);
        _cookieContainer.Add(uri, new Cookie("oAuthToken", session.AuthToken));
        _cookieContainer.Add(uri, new Cookie("signKey", session.HashKey));
        _cookieContainer.Add(uri, new Cookie("BIZCUBE_AT", session.AuthToken));
        _cookieContainer.Add(uri, new Cookie("BIZCUBE_HK", session.HashKey));
        _cookieContainer.Add(uri, new Cookie("BIZCUBE_TYPE", "WEB"));
    }

    private void ConfigureHttpClient()
    {
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ko-KR,ko;q=0.9,en-US;q=0.8,en;q=0.7");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
    }

    private static DateTime? ParseDate(string value)
    {
        return DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
            ? date
            : null;
    }

    private static string GetStatusCode(JsonElement element)
    {
        string code = GetString(element, "DOC_STS");
        if (!string.IsNullOrWhiteSpace(code))
        {
            return code;
        }

        int numeric = GetInt32(element, "DOC_STS");
        return numeric == 0 ? string.Empty : numeric.ToString(CultureInfo.InvariantCulture);
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }

        return property.ToString();
    }

    private static int GetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value))
        {
            return value;
        }

        return int.TryParse(property.ToString(), out int parsedValue) ? parsedValue : 0;
    }

    private static int GetInt32FromString(JsonElement element, string propertyName)
    {
        string value = GetString(element, propertyName);
        return int.TryParse(value, out int parsedValue) ? parsedValue : 0;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("cur_date")]
        public string CurrentDate { get; set; } = string.Empty;

        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;
    }

    private sealed class UnsignedToken
    {
        public string Token { get; set; } = string.Empty;
        public string CurrentDate { get; set; } = string.Empty;
        public string TransactionId { get; set; } = string.Empty;
    }
}
