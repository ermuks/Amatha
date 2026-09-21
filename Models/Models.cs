namespace Amaranth10API.Models;

public sealed class AmaranthSession
{
    public string AuthToken { get; set; } = string.Empty;
    public string HashKey { get; set; } = string.Empty;
    public int SessionTime { get; set; }
    public string EmployeeSequence { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string CompanySequence { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string DepartmentSequence { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string PositionName { get; set; } = string.Empty;
}

public sealed class WorkSchedule
{
    public DateTime Date { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ApprovalState { get; set; } = string.Empty;
    public string FormName { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string DocumentId { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string BusinessTripLocation { get; set; } = string.Empty;
    public string BusinessTripPurpose { get; set; } = string.Empty;
    public string Task { get; set; } = string.Empty;
}

public sealed class ApprovalDocumentPage
{
    public int TotalCount { get; set; }
    public List<ApprovalDocumentSummary> Documents { get; set; } = new();
}

public sealed class ApprovalDocumentSummary
{
    public int DocumentId { get; set; }
    public int FormId { get; set; }
    public string FormName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string DocumentStatus { get; set; } = string.Empty;
    public string DocumentStatusCode { get; set; } = string.Empty;
    public string DocumentAuth { get; set; } = string.Empty;
    public DateTime? CreatedDate { get; set; }
    public DateTime? ActionTime { get; set; }
}

public sealed class BusinessTripDocument
{
    public int DocumentId { get; set; }
    public int FormId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string DocumentStatus { get; set; } = string.Empty;
    public string DocumentStatusCode { get; set; } = string.Empty;
    public DateTime? CreatedDate { get; set; }
    public DateTime? ActionTime { get; set; }
    public DateTime? TripStartDate { get; set; }
    public DateTime? TripEndDate { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public string ContentsWord { get; set; } = string.Empty;
    public string DocContents { get; set; } = string.Empty;
    public string ProjectCode { get; set; } = string.Empty;
    public List<HolidayWorkDay> WorkDays { get; set; } = new();
    public List<DateTime> CoveredDates { get; set; } = new();
    public bool IsCancellation { get; set; }
}

public sealed class HolidayWorkDay
{
    public DateTime WorkDate { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
}

public sealed class BusinessTripReport
{
    public int DocumentId { get; set; }
    public int FormId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string DocumentStatus { get; set; } = string.Empty;
    public string DocumentStatusCode { get; set; } = string.Empty;
    public DateTime? CreatedDate { get; set; }
    public DateTime? ActionTime { get; set; }
    public DateTime? TripStartDate { get; set; }
    public DateTime? TripEndDate { get; set; }
    public string ContentsWord { get; set; } = string.Empty;
    public string DocContents { get; set; } = string.Empty;
    public List<HolidayWorkEntry> HolidayWorks { get; set; } = new();
}

public sealed class HolidayWorkEntry
{
    public DateTime WorkDate { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public string CompensationType { get; set; } = string.Empty;
    public string SubstituteHolidayDateText { get; set; } = string.Empty;
}

public sealed class SubstituteHolidayRequest
{
    public int DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Month { get; set; }
    public int Day { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
}

public sealed class SubstituteHolidayIssue
{
    public bool IsTimeMismatch { get; init; }
    public DateTime WorkDate { get; init; }
    public string ReportStartTime { get; init; } = string.Empty;
    public string ReportEndTime { get; init; } = string.Empty;
    public string RequestStartTime { get; init; } = string.Empty;
    public string RequestEndTime { get; init; } = string.Empty;
    public string ReportTitle { get; init; } = string.Empty;
    public string RequestTitle { get; init; } = string.Empty;
}

public sealed class MissingReportPeriod
{
    public string Kind { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public int DayCount => (EndDate.Date - StartDate.Date).Days + 1;
    public BusinessTripDocument? RelatedApplication { get; init; }
}

public sealed class ReportDraftFill
{
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public int DayCount => (EndDate.Date - StartDate.Date).Days + 1;
    public string StartTime { get; init; } = string.Empty;
    public string EndTime { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public List<ReportHolidayDayFill> HolidayDays { get; init; } = new();
}

public sealed class ReportHolidayDayFill
{
    public int Year { get; init; }
    public int Month { get; init; }
    public int Day { get; init; }
    public string Weekday { get; init; } = string.Empty;
    public string StartTime { get; init; } = string.Empty;
    public string EndTime { get; init; } = string.Empty;
}
