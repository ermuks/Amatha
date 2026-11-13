using System.Globalization;
using System.Text.RegularExpressions;
using Amaranth10API.Models;

namespace Amaranth10API.Services;

/// <summary>
/// 교육 신청서(사외교육신청서 · 영어교육계획서 …)와 교육결과보고서를 맞춰, 보고서를 쓰지 않은 교육을 찾습니다.
///
/// 보고서 제목은 "[교육결과보고서] 부서 이름"처럼 교육명이 없으므로 이렇게 맞춥니다.
///  1) 보고서 본문(또는 제목)에 신청서의 교육명이 들어 있으면 그 신청서의 보고서
///  2) 남은 것은 먼저 끝난 교육부터, 교육이 끝난 날 이후에 쓴 가장 이른 보고서와 하나씩 짝지음
///  3) 그래도 남으면 교육 시작일 이후에 쓴 보고서까지 허용 (교육 중에 미리 쓴 경우)
/// 짝이 없는 신청서가 "미작성"입니다. 교육이 끝나기 전에는 목록에만 보이고 알림은 보내지 않습니다.
/// </summary>
public sealed partial class AmaranthClient
{
    public List<BusinessTripDocument> EducationApplications { get; } = new();
    public List<EducationReport> EducationReports { get; } = new();

    /// <summary>[사외교육신청서], [영어교육계획서], [사내교육신청서] 처럼 "교육" + "신청서/계획서"</summary>
    internal static bool IsEducationApplication(ApprovalDocumentSummary summary)
    {
        if (IsEducationReport(summary) || IsLeaveUsageApplication(summary)) return false;
        return IsEducationApplicationText(summary.FormName) || IsEducationApplicationText(summary.Title);
    }

    internal static bool IsEducationReport(ApprovalDocumentSummary summary) =>
        Contains(summary.FormName, "교육결과보고서") || Contains(summary.Title, "교육결과보고서");

    private static bool IsEducationApplicationText(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Regex.IsMatch(value, @"교육\s*(신청서|계획서)") &&
        !value.Contains("교육결과보고서", StringComparison.Ordinal);

    private static bool Contains(string? value, string keyword) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private async Task<BusinessTripDocument> GetEducationApplicationDetailAsync(AmaranthSession session, ApprovalDocumentSummary summary)
    {
        (string contentsWord, string docContents) = await GetApprovalDetailContentsAsync(session, summary, "교육신청서");
        (DateTime? start, DateTime? end) = ParseEducationPeriod(contentsWord + "\n" + ConvertHtmlToText(docContents));
        return CreateApplicationDocument(summary, contentsWord, docContents, start, end, string.Empty, string.Empty);
    }

    private async Task<EducationReport> GetEducationReportDetailAsync(AmaranthSession session, ApprovalDocumentSummary summary)
    {
        (string contentsWord, string docContents) = await GetApprovalDetailContentsAsync(session, summary, "교육결과보고서");
        return new EducationReport
        {
            DocumentId = summary.DocumentId,
            Title = summary.Title,
            DocumentStatus = summary.DocumentStatus,
            CreatedDate = summary.CreatedDate,
            ActionTime = summary.ActionTime,
            ContentsText = contentsWord + "\n" + ConvertHtmlToText(docContents)
        };
    }

    /// <summary>
    /// 신청서 본문에서 교육 기간을 찾습니다.
    /// "교육기간/교육일시/교육일자/교육일정/일시/기간" 바로 뒤의 날짜 → 없으면 본문의 "날짜 ~ 날짜" → 없으면 null.
    /// </summary>
    internal static (DateTime? Start, DateTime? End) ParseEducationPeriod(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        string normalized = NormalizeVisibleText(text);

        foreach (Match label in Regex.Matches(normalized, @"(?:교육|수강|연수)\s*(?:기간|일시|일자|일정|날짜)|(?<![가-힣])(?:기\s*간|일\s*시)(?![가-힣])"))
        {
            int from = label.Index + label.Length;
            string window = normalized.Substring(from, Math.Min(90, normalized.Length - from));
            List<DateTime> dates = FindDates(window);
            if (dates.Count > 0) return (dates.Min(), dates.Max());
        }

        List<(DateTime Start, DateTime End)> ranges = ParseFullDateRanges(normalized);
        if (ranges.Count > 0) return (ranges.Min(r => r.Start), ranges.Max(r => r.End));
        return (null, null);
    }

    /// <summary>2026-09-07, 2026.9.7, 2026/09/07, 2026년 9월 7일 (같은 해의 "~ 9월 20일"도 앞 날짜의 해로)</summary>
    internal static List<DateTime> FindDates(string text)
    {
        List<DateTime> dates = new();
        int? lastYear = null;
        foreach (Match match in Regex.Matches(text,
                     @"(?:(\d{4})\s*[-./년]\s*)?(\d{1,2})\s*[-./월]\s*(\d{1,2})\s*일?"))
        {
            int year;
            if (match.Groups[1].Success) year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            else if (lastYear.HasValue) year = lastYear.Value;
            else continue; // 해가 없는 첫 날짜(시간 "10.30" 등)는 믿지 않음

            if (!int.TryParse(match.Groups[2].Value, out int month) || !int.TryParse(match.Groups[3].Value, out int day)) continue;
            if (month is < 1 or > 12 || day is < 1 or > 31 || year is < 2000 or > 2100) continue;
            try
            {
                dates.Add(new DateTime(year, month, day));
                lastYear = year;
            }
            catch (ArgumentOutOfRangeException)
            {
                // 2월 30일처럼 없는 날짜는 건너뜀
            }
        }

        return dates;
    }

    /// <summary>"[사외교육신청서] 디지털트윈팀 최지수 (Unity Industry 기술 세미나)" → ("사외교육", "Unity Industry 기술 세미나")</summary>
    internal static (string KindName, string CourseName) ParseEducationTitle(string title)
    {
        title ??= string.Empty;
        string kind = "교육";
        Match bracket = Regex.Match(title, @"\[\s*([^\]]*?교육)\s*(?:신청서|계획서)\s*\]");
        if (bracket.Success) kind = bracket.Groups[1].Value.Trim();

        string course = string.Empty;
        Match paren = Regex.Match(title, @"\(([^()]*)\)\s*$");
        if (paren.Success) course = paren.Groups[1].Value.Trim();
        return (kind, course);
    }

    /// <summary>보고서를 쓰지 않은 교육 신청서 (아직 시작 전·진행 중인 것도 포함, 알림 여부는 화면에서 날짜로 정함)</summary>
    public List<MissingEducationReport> GetMissingEducationReports() =>
        MatchEducationReports(EducationApplications, EducationReports);

    internal static List<MissingEducationReport> MatchEducationReports(
        IReadOnlyList<BusinessTripDocument> applications,
        IReadOnlyList<EducationReport> reports)
    {
        var candidates = applications
            .Where(app => !app.IsCancellation)
            .Select(app =>
            {
                (string kind, string course) = ParseEducationTitle(app.Title);
                DateTime? fallback = app.ActionTime ?? app.CreatedDate;
                bool guessed = !app.TripStartDate.HasValue && !app.TripEndDate.HasValue;
                DateTime start = (app.TripStartDate ?? app.TripEndDate ?? fallback ?? DateTime.Today).Date;
                DateTime end = (app.TripEndDate ?? app.TripStartDate ?? fallback ?? DateTime.Today).Date;
                if (end < start) (start, end) = (end, start);
                return new MissingEducationReport
                {
                    Application = app, KindName = kind, CourseName = course,
                    StartDate = start, EndDate = end, PeriodGuessed = guessed
                };
            })
            .OrderBy(item => item.StartDate).ThenBy(item => item.Application.DocumentId)
            .ToList();

        HashSet<EducationReport> used = new();
        HashSet<MissingEducationReport> matched = new();

        // 1) 보고서 본문·제목에 교육명이 있으면 그 신청서와 짝
        foreach (MissingEducationReport item in candidates)
        {
            List<string> keys = CourseKeys(item);
            if (keys.Count == 0) continue;
            EducationReport? report = reports
                .Where(r => !used.Contains(r))
                .OrderBy(r => r.WrittenDate ?? DateTime.MaxValue)
                .FirstOrDefault(r => keys.Any(key => Squash(r.ContentsText + r.Title).Contains(key)));
            if (report == null) continue;
            used.Add(report);
            matched.Add(item);
        }

        // 2) 보고서는 보통 교육이 끝난 뒤 쓰므로, 먼저 끝난 교육부터 "끝난 날 이후에 쓴 가장 이른 보고서"와 하나씩
        // 3) 그래도 남으면 "시작한 날 이후에 쓴 보고서"까지 (교육 중에 미리 쓴 경우)
        foreach (Func<MissingEducationReport, DateTime> from in new Func<MissingEducationReport, DateTime>[] { c => c.EndDate, c => c.StartDate })
        {
            // 기간을 결재일로 짐작한 신청서는 실제 교육 기간이 있는 신청서 다음에
            foreach (MissingEducationReport item in candidates.Where(c => !matched.Contains(c))
                         .OrderBy(c => c.PeriodGuessed).ThenBy(c => c.EndDate).ToList())
            {
                DateTime earliest = from(item);
                EducationReport? report = reports
                    .Where(r => !used.Contains(r) && r.WrittenDate.HasValue && r.WrittenDate.Value.Date >= earliest)
                    .OrderBy(r => r.WrittenDate)
                    .FirstOrDefault();
                if (report == null) continue;
                used.Add(report);
                matched.Add(item);
            }
        }

        return candidates.Where(c => !matched.Contains(c)).ToList();
    }

    /// <summary>교육명을 보고서에서 찾을 때 쓸 말 (공백·기호를 뺀 소문자). 너무 짧으면 쓰지 않음</summary>
    private static List<string> CourseKeys(MissingEducationReport item)
    {
        List<string> keys = new();
        void Add(string value)
        {
            string key = Squash(value);
            if (key.Length >= 4 && !keys.Contains(key)) keys.Add(key);
        }

        if (!string.IsNullOrWhiteSpace(item.CourseName))
        {
            Add(item.CourseName);
            Add(item.CourseName.Split(',', '·', '-', '|')[0]);
        }
        else if (item.KindName != "교육")
        {
            // [영어교육계획서]처럼 교육명이 없으면 종류 이름(영어교육)으로
            Add(item.KindName);
        }

        return keys;
    }

    private static string Squash(string value) =>
        Regex.Replace(value ?? string.Empty, @"[\s\p{P}\p{S}]+", string.Empty).ToLowerInvariant();
}
