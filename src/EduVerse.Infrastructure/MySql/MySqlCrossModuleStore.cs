using System.Text.Json;
using EduVerse.Application.Workflows;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

/// <summary>One InnoDB transaction for each cross-module transition and its outbox record.</summary>
public sealed class MySqlCrossModuleStore(string connectionString) : ICrossModuleStore
{
    private static string Id(Guid id) => id.ToString("D");
    private static void Args(MySqlCommand c, params (string Name, object? Value)[] args)
    {
        foreach (var (name, value) in args) c.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
    private static async Task<object?> Scalar(MySqlConnection db, MySqlTransaction? tx, string sql,
        CancellationToken ct, params (string Name, object? Value)[] args)
    {
        await using var cmd = new MySqlCommand(sql, db, tx);
        Args(cmd, args);
        return await cmd.ExecuteScalarAsync(ct);
    }
    private static async Task<int> Execute(MySqlConnection db, MySqlTransaction tx, string sql,
        CancellationToken ct, params (string Name, object? Value)[] args)
    {
        await using var cmd = new MySqlCommand(sql, db, tx);
        Args(cmd, args);
        return await cmd.ExecuteNonQueryAsync(ct);
    }
    public async Task<bool> IsAdminAsync(TenantIdentity actor, CancellationToken ct)
    {
        await using var db = new MySqlConnection(connectionString);
        await db.OpenAsync(ct);
        return Convert.ToInt32(await Scalar(db, null, """
            SELECT EXISTS(SELECT 1 FROM tenant_memberships m
             JOIN identity_users u ON u.id=m.subject_id AND u.is_active=1
             JOIN tenants t ON t.id=m.tenant_id AND t.is_active=1
             JOIN institution_subscriptions s ON s.tenant_id=t.id AND s.status='ACTIVE'
               AND (s.expires_utc IS NULL OR s.expires_utc>UTC_TIMESTAMP(6))
             JOIN subscription_plan_features f ON f.plan_code=s.plan_code AND f.feature_code='hrms'
             JOIN subscription_plans p ON p.plan_code=s.plan_code AND p.is_active=1
             WHERE m.tenant_id=@t AND m.subject_id=@a AND m.is_active=1 AND m.role_code='ADMIN')
            """, ct, ("@t", Id(actor.TenantId)), ("@a", actor.SubjectId))) == 1;
    }
    public async Task<ClearanceSummary?> Clearance(Guid tenant, Guid campus, Guid employee, CancellationToken ct)
    {
        await using var db = new MySqlConnection(connectionString);
        await db.OpenAsync(ct);
        var found = await Scalar(db, null,
            "SELECT id FROM hr_employees WHERE tenant_id=@t AND campus_id=@c AND id=@e", ct,
            ("@t", Id(tenant)), ("@c", Id(campus)), ("@e", Id(employee)));
        if (found is null) return null;
        var count = Convert.ToInt32(await Scalar(db, null, """
            SELECT COUNT(*) FROM ams_assets
            WHERE tenant_id=@t AND campus_id=@c AND employee_id=@e
              AND status='ALLOCATED'
            """, ct, ("@t", Id(tenant)), ("@c", Id(campus)), ("@e", Id(employee))));
        return new ClearanceSummary(employee, count);
    }
    private async Task<WorkflowReceipt> Run(Guid tenant, string actor, Guid campus,
        Guid? employee, Guid? purchase, string operation, string key, byte[] digest,
        Func<MySqlConnection, MySqlTransaction, CancellationToken, Task<string>> change, CancellationToken ct)
    {
        await using var db = new MySqlConnection(connectionString);
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        // Serialize idempotency checks, seats and staff transitions at tenant scope.
        if (await Scalar(db, tx,
            "SELECT id FROM tenants WHERE id=@t AND is_active=1 FOR UPDATE", ct,
            ("@t", Id(tenant))) is null) throw new WorkflowScopeException();
        if (Convert.ToInt32(await Scalar(db, tx,
            "SELECT EXISTS(SELECT 1 FROM campuses WHERE tenant_id=@t AND id=@c AND is_active=1)", ct,
            ("@t", Id(tenant)), ("@c", Id(campus)))) != 1) throw new WorkflowScopeException();
        await using (var existing = new MySqlCommand("""
            SELECT id,campus_id,request_sha256 FROM workflow_operations
             WHERE tenant_id=@t AND operation_code=@op AND idempotency_key=@key
            """, db, tx))
        {
            Args(existing, ("@t", Id(tenant)), ("@op", operation), ("@key", key));
            await using var reader = await existing.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var id = reader.GetGuid(0);
                var sameCampus = reader.GetGuid(1) == campus;
                var samePayload = reader.GetFieldValue<byte[]>(2).AsSpan().SequenceEqual(digest);
                if (!sameCampus || !samePayload) throw new WorkflowConflictException("Idempotency key reused with different input.");
                await reader.DisposeAsync();
                await tx.CommitAsync(ct);
                return new WorkflowReceipt(id, "ALREADY_APPLIED");
            }
        }
        var status = await change(db, tx, ct);
        var opId = Guid.NewGuid();
        await Execute(db, tx, """
            INSERT INTO workflow_operations
             (id,tenant_id,campus_id,operation_code,idempotency_key,request_sha256,employee_id,purchase_id,actor_id)
            VALUES(@id,@t,@c,@op,@key,@hash,@employee,@purchase,@actor)
            """, ct, ("@id", Id(opId)), ("@t", Id(tenant)), ("@c", Id(campus)), ("@op", operation),
            ("@key", key), ("@hash", digest), ("@employee", employee is null ? null : Id(employee.Value)),
            ("@purchase", purchase is null ? null : Id(purchase.Value)), ("@actor", actor));
        var eventType = operation switch
        {
            "HR_IDENTITY_BIND" => "workflow.hr.identity_bound",
            "HR_EXIT_CLEARANCE" => "workflow.hr.exited",
            _ => "workflow.procurement.finance_requested"
        };
        await Execute(db, tx, """
            INSERT INTO integration_outbox(id,tenant_id,type,payload_json)
            VALUES(@id,@t,@type,@payload)
            """, ct, ("@id", Id(Guid.NewGuid())), ("@t", Id(tenant)),
            ("@type", eventType), ("@payload", JsonSerializer.Serialize(new
            {
                operationId = opId, campusId = campus, employeeId = employee, purchaseId = purchase
            })));
        await tx.CommitAsync(ct);
        return new WorkflowReceipt(opId, status);
    }
    public Task<WorkflowReceipt> BindIdentity(Guid tenant, string actor, EmployeeWorkflowRequest r,
        string key, byte[] digest, CancellationToken ct) =>
        Run(tenant, actor, r.CampusId, r.EmployeeId, null, "HR_IDENTITY_BIND", key, digest,
            async (db, tx, token) =>
            {
                var user = Id(r.UserId!.Value);
                if (Convert.ToInt32(await Scalar(db, tx, """
                    SELECT EXISTS(SELECT 1 FROM tenant_memberships m JOIN identity_users u
                    ON u.id=m.subject_id AND u.is_active=1
                    WHERE m.tenant_id=@t AND m.subject_id=@u AND m.is_active=1)
                    """, token, ("@t", Id(tenant)), ("@u", user))) != 1)
                    throw new WorkflowScopeException();
                var affected = await Execute(db, tx, """
                    UPDATE hr_employees SET user_subject_id=@u
                    WHERE id=@e AND tenant_id=@t AND campus_id=@c AND status='ONBOARDING'
                      AND user_subject_id IS NULL
                    """, token, ("@u", user), ("@e", Id(r.EmployeeId)), ("@t", Id(tenant)),
                    ("@c", Id(r.CampusId)));
                if (affected != 1) throw new WorkflowConflictException("Employee must be unlinked and onboarding.");
                await Execute(db, tx, """
                    INSERT INTO identity_campus_access(tenant_id,subject_id,campus_id)
                    VALUES(@t,@u,@c) ON DUPLICATE KEY UPDATE campus_id=VALUES(campus_id)
                    """, token, ("@t", Id(tenant)), ("@u", user), ("@c", Id(r.CampusId)));
                return "BOUND";
            }, ct);
    public Task<WorkflowReceipt> Exit(Guid tenant, string actor, EmployeeWorkflowRequest r,
        string key, byte[] digest, CancellationToken ct) =>
        Run(tenant, actor, r.CampusId, r.EmployeeId, null, "HR_EXIT_CLEARANCE", key, digest,
            async (db, tx, token) =>
            {
                string? state;
                string? subject;
                await using (var command = new MySqlCommand("""
                    SELECT status,user_subject_id FROM hr_employees
                    WHERE id=@e AND tenant_id=@t AND campus_id=@c FOR UPDATE
                    """, db, tx))
                {
                    Args(command, ("@e", Id(r.EmployeeId)), ("@t", Id(tenant)), ("@c", Id(r.CampusId)));
                    await using var reader = await command.ExecuteReaderAsync(token);
                    if (!await reader.ReadAsync(token)) throw new WorkflowScopeException();
                    state = reader.GetString(0);
                    subject = reader.IsDBNull(1) ? null : reader.GetString(1);
                }
                if (state is not ("ACTIVE" or "ON_LEAVE"))
                    throw new WorkflowConflictException("Employee must be active or on leave.");
                if (subject == actor) throw new WorkflowConflictException("Administrator cannot offboard own account.");
                var assets = Convert.ToInt32(await Scalar(db, tx, """
                    SELECT COUNT(*) FROM ams_assets
                    WHERE tenant_id=@t AND campus_id=@c AND employee_id=@e
                    """, token, ("@t", Id(tenant)), ("@c", Id(r.CampusId)), ("@e", Id(r.EmployeeId))));
                if (assets > 0) throw new WorkflowConflictException("Return or transfer all allocated assets before offboarding.");
                await Execute(db, tx, """
                    UPDATE hr_employees SET status='EXITED',exited_on=UTC_DATE()
                    WHERE tenant_id=@t AND campus_id=@c AND id=@e
                    """, token, ("@t", Id(tenant)), ("@c", Id(r.CampusId)), ("@e", Id(r.EmployeeId)));
                await Execute(db, tx, """
                    INSERT INTO hr_employee_events
                     (id,tenant_id,campus_id,employee_id,event_type,note,actor_subject_id)
                    VALUES(@id,@t,@c,@e,'EXITED','Cross-module clearance completed',@a)
                    """, token, ("@id", Id(Guid.NewGuid())), ("@t", Id(tenant)),
                    ("@c", Id(r.CampusId)), ("@e", Id(r.EmployeeId)), ("@a", actor));
                if (subject is not null)
                {
                    await Execute(db, tx, """
                        UPDATE tenant_memberships SET is_active=0
                        WHERE tenant_id=@t AND subject_id=@u AND role_code='MEMBER'
                        """, token, ("@t", Id(tenant)), ("@u", subject));
                    // Revoke existing access immediately, rather than waiting for a worker.
                    await Execute(db, tx, """
                        UPDATE identity_sessions SET revoked_utc=UTC_TIMESTAMP(6)
                        WHERE tenant_id=@t AND user_id=@u AND revoked_utc IS NULL
                        """, token, ("@t", Id(tenant)), ("@u", subject));
                    await Execute(db, tx, """
                        DELETE FROM identity_campus_access WHERE tenant_id=@t AND subject_id=@u
                        """, token, ("@t", Id(tenant)), ("@u", subject));
                }
                return "EXITED";
            }, ct);
    public Task<WorkflowReceipt> Purchase(Guid tenant, string actor, PurchaseWorkflowRequest r,
        string key, byte[] digest, CancellationToken ct) =>
        Run(tenant, actor, r.CampusId, null, r.PurchaseId, "PROCUREMENT_FINANCE", key, digest,
            async (db, tx, token) =>
            {
                var state = await Scalar(db, tx, """
                    SELECT status FROM ams_purchase_orders
                    WHERE tenant_id=@t AND campus_id=@c AND id=@p FOR UPDATE
                    """, token, ("@t", Id(tenant)), ("@c", Id(r.CampusId)), ("@p", Id(r.PurchaseId)));
                if (state is null) throw new WorkflowScopeException();
                if ((string)state != "ORDERED")
                    throw new WorkflowConflictException("Only ordered purchases may be submitted to finance.");
                await Execute(db, tx, """
                    INSERT INTO workflow_purchase_finance
                     (tenant_id,campus_id,purchase_id,status,requested_by)
                    VALUES(@t,@c,@p,'PENDING',@a)
                    """, token, ("@t", Id(tenant)), ("@c", Id(r.CampusId)),
                    ("@p", Id(r.PurchaseId)), ("@a", actor));
                return "PENDING_FINANCE";
            }, ct);
}
