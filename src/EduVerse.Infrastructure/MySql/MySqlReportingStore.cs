using EduVerse.Application.Reporting;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

/// <summary>One repeatable-read snapshot; every aggregate is parameterized by JWT tenant and campus.</summary>
public sealed class MySqlReportingStore(string connectionString) : IReportingStore
{
    private static void Scope(MySqlCommand command, Guid tenantId, Guid campusId,
        DateOnly from, DateOnly to)
    {
        command.Parameters.AddWithValue("@tenant", tenantId.ToString("D"));
        command.Parameters.AddWithValue("@campus", campusId.ToString("D"));
        command.Parameters.AddWithValue("@from", from.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@until", to.AddDays(1).ToDateTime(TimeOnly.MinValue));
    }
    public async Task<bool> IsAdminAsync(TenantIdentity actor, CancellationToken ct)
    {
        await using var db = new MySqlConnection(connectionString);
        await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("""
            SELECT EXISTS(SELECT 1 FROM tenant_memberships m
            JOIN identity_users u ON u.id=m.subject_id AND u.is_active=1
            JOIN tenants t ON t.id=m.tenant_id AND t.is_active=1
            JOIN institution_subscriptions s ON s.tenant_id=t.id AND s.status='ACTIVE'
                AND (s.expires_utc IS NULL OR s.expires_utc>UTC_TIMESTAMP(6))
            JOIN subscription_plan_features f ON f.plan_code=s.plan_code AND f.feature_code='reports'
            JOIN subscription_plans p ON p.plan_code=s.plan_code AND p.is_active=1
            WHERE m.tenant_id=@tenant AND m.subject_id=@user
              AND m.is_active=1 AND m.role_code='ADMIN')
            """, db);
        cmd.Parameters.AddWithValue("@tenant", actor.TenantId.ToString("D"));
        cmd.Parameters.AddWithValue("@user", actor.SubjectId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }
    public async Task<InstitutionAnalytics?> DashboardAsync(Guid tenantId, Guid campusId,
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var db = new MySqlConnection(connectionString);
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await using (var campus = new MySqlCommand("""
            SELECT EXISTS(SELECT 1 FROM campuses WHERE tenant_id=@tenant AND id=@campus AND is_active=1)
            """, db, tx))
        {
            Scope(campus, tenantId, campusId, from, to);
            if (Convert.ToInt32(await campus.ExecuteScalarAsync(ct)) != 1) return null;
        }
        // Counts are current state. Financial values represent posted activity up to report end
        // (not filtered by start); this makes as-of receivables mathematically consistent.
        InstitutionKpis kpis;
        await using (var totals = new MySqlCommand("""
            SELECT
             (SELECT COUNT(*) FROM students WHERE tenant_id=@tenant AND campus_id=@campus AND status='ACTIVE'),
             (SELECT COUNT(*) FROM student_enrollments WHERE tenant_id=@tenant AND campus_id=@campus AND status='ACTIVE'),
             (SELECT COUNT(*) FROM hr_employees WHERE tenant_id=@tenant AND campus_id=@campus AND status IN ('ACTIVE','ON_LEAVE')),
             (SELECT COUNT(*) FROM ams_assets WHERE tenant_id=@tenant AND campus_id=@campus AND status='ALLOCATED'),
             (SELECT COUNT(*) FROM admission_applications WHERE tenant_id=@tenant AND campus_id=@campus AND status='SUBMITTED'),
             (SELECT COALESCE(SUM(gross_amount),0) FROM finance_student_charges
               WHERE tenant_id=@tenant AND campus_id=@campus AND created_utc<@until),
             (SELECT COALESCE(SUM(concession_amount),0) FROM finance_student_charges
               WHERE tenant_id=@tenant AND campus_id=@campus AND created_utc<@until),
             (SELECT COALESCE(SUM(amount),0) FROM finance_payments
               WHERE tenant_id=@tenant AND campus_id=@campus AND collected_utc<@until),
             (SELECT COALESCE(SUM(amount),0) FROM finance_refunds
               WHERE tenant_id=@tenant AND campus_id=@campus AND refunded_utc<@until)
            """, db, tx))
        {
            Scope(totals, tenantId, campusId, from, to);
            await using var reader = await totals.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Analytics snapshot unavailable.");
            var gross = reader.GetDecimal(5);
            var concessions = reader.GetDecimal(6);
            var collected = reader.GetDecimal(7);
            var refunds = reader.GetDecimal(8);
            kpis = new InstitutionKpis(campusId, reader.GetInt32(0), reader.GetInt32(1),
                reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
                gross, concessions, collected, refunds, gross - concessions - collected + refunds);
        }
        var monthly = new SortedDictionary<(int Year, int Month), (int Enrollments, int Payments, decimal Collected)>();
        var cursor = new DateOnly(from.Year, from.Month, 1);
        while (cursor <= to)
        {
            monthly[(cursor.Year, cursor.Month)] = (0, 0, 0m);
            cursor = cursor.AddMonths(1);
        }
        await using (var enroll = new MySqlCommand("""
            SELECT YEAR(created_utc),MONTH(created_utc),COUNT(*)
            FROM student_enrollments WHERE tenant_id=@tenant AND campus_id=@campus
                AND created_utc>=@from AND created_utc<@until
            GROUP BY YEAR(created_utc),MONTH(created_utc)
            """, db, tx))
        {
            Scope(enroll, tenantId, campusId, from, to);
            await using var reader = await enroll.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var key = (reader.GetInt32(0), reader.GetInt32(1));
                var old = monthly[key];
                monthly[key] = (reader.GetInt32(2), old.Payments, old.Collected);
            }
        }
        await using (var payments = new MySqlCommand("""
            SELECT YEAR(collected_utc),MONTH(collected_utc),COUNT(*),COALESCE(SUM(amount),0)
            FROM finance_payments WHERE tenant_id=@tenant AND campus_id=@campus
                AND collected_utc>=@from AND collected_utc<@until
            GROUP BY YEAR(collected_utc),MONTH(collected_utc)
            """, db, tx))
        {
            Scope(payments, tenantId, campusId, from, to);
            await using var reader = await payments.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var key = (reader.GetInt32(0), reader.GetInt32(1));
                var old = monthly[key];
                monthly[key] = (old.Enrollments, reader.GetInt32(2), reader.GetDecimal(3));
            }
        }
        await tx.CommitAsync(ct);
        return new InstitutionAnalytics(kpis,
            monthly.Select(row => new ReportMonthlyRow(row.Key.Year, row.Key.Month,
                row.Value.Enrollments, row.Value.Payments, row.Value.Collected)).ToArray(),
            DateTime.UtcNow);
    }
}
