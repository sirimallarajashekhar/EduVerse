using EduVerse.Application.Tenancy;
using EduVerse.Domain;

namespace EduVerse.Application.Reporting;

public sealed record InstitutionKpis(
    Guid CampusId, int ActiveStudents, int ActiveEnrollments,
    int ActiveStaff, int AllocatedAssets, int PendingAdmissions,
    decimal ChargedInr, decimal ConcessionsInr, decimal CollectedInr,
    decimal RefundedInr, decimal OutstandingInr);
public sealed record ReportMonthlyRow(int Year, int Month, int Enrollments, int Payments, decimal CollectedInr);
public sealed record InstitutionAnalytics(InstitutionKpis Kpis, IReadOnlyList<ReportMonthlyRow> Months, DateTime GeneratedUtc);
public sealed class ReportingScopeException : Exception;

public interface IReportingStore
{
    Task<bool> IsAdminAsync(TenantIdentity actor, CancellationToken ct);
    Task<InstitutionAnalytics?> DashboardAsync(Guid tenantId, Guid campusId, DateOnly from, DateOnly to,
        CancellationToken ct);
}
public sealed class ReportingService(IReportingStore store, ITenantContext context)
{
    public async Task<InstitutionAnalytics> Dashboard(Guid campusId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (campusId == Guid.Empty || from == default || to == default ||
            to < from || to.DayNumber - from.DayNumber > 366 ||
            to > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            throw new ArgumentException("A valid campus and reporting range of at most 366 days is required.");
        if (!context.TryGet(out var actor) || actor is null ||
            !await store.IsAdminAsync(actor, ct))
            throw new UnauthorizedAccessException("Active reporting administrator is required.");
        return await store.DashboardAsync(actor.TenantId, campusId, from, to, ct)
            ?? throw new ReportingScopeException();
    }
    public static byte[] Csv(InstitutionAnalytics report)
    {
        // Fixed schema and numeric values only: never include user-authored text or raw PII in export.
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("year,month,enrollments,payments,collected_inr");
        foreach (var row in report.Months)
            csv.Append(row.Year).Append(',').Append(row.Month).Append(',')
                .Append(row.Enrollments).Append(',').Append(row.Payments).Append(',')
                .Append(row.CollectedInr.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine();
        return System.Text.Encoding.UTF8.GetBytes(csv.ToString());
    }
}
