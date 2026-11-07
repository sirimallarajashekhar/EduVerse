using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

public sealed partial class MySqlInstitutionAdminStore
{
    public async Task<IReadOnlyList<SubscriptionPlan>> PlansAsync(CancellationToken ct)
    {
        var plans=new List<SubscriptionPlan>();
        await using var db=Connection();
        await db.OpenAsync(ct);
        await using var command=new MySqlCommand("""
            SELECT plan_code,display_name,description FROM subscription_plans
            WHERE is_active=1 ORDER BY plan_code
            """,db);
        await using var reader=await command.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            plans.Add(new SubscriptionPlan(reader.GetString(0),reader.GetString(1),reader.GetString(2)));
        return plans;
    }

    public async Task<SubscriptionStatus> SubscriptionAsync(Guid tenantId,CancellationToken ct)
    {
        string? code=null;
        string status="UNASSIGNED";
        DateTime? expires=null;
        await using var db=Connection();
        await db.OpenAsync(ct);
        await using (var command=new MySqlCommand("""
            SELECT plan_code,status,expires_utc FROM institution_subscriptions
            WHERE tenant_id=@tenant
            """,db))
        {
            Add(command,("@tenant",tenantId.ToString("D")));
            await using var reader=await command.ExecuteReaderAsync(ct);
            if(await reader.ReadAsync(ct))
            {
                code=reader.GetString(0);
                status=reader.GetString(1);
                expires=reader.IsDBNull(2)?null:reader.GetDateTime(2);
            }
        }
        var features=new List<string>();
        if(code is not null && status=="ACTIVE" && (expires is null || expires> DateTime.UtcNow))
        {
            await using var command=new MySqlCommand("""
                SELECT pf.feature_code FROM subscription_plan_features pf
                JOIN subscription_plans p ON p.plan_code=pf.plan_code AND p.is_active=1
                WHERE pf.plan_code=@plan ORDER BY pf.feature_code
                """,db);
            Add(command,("@plan",code));
            await using var reader=await command.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))features.Add(reader.GetString(0));
        }
        return new SubscriptionStatus(code,status,expires,features);
    }

    public async Task<InstitutionDashboard> DashboardAsync(Guid tenantId,CancellationToken ct)
    {
        await using var db=Connection();
        await db.OpenAsync(ct);
        await using var command=new MySqlCommand("""
            SELECT t.display_name,t.kind,
                COALESCE(p.onboarding_status,'DRAFT'),
                (SELECT COUNT(*) FROM campuses c WHERE c.tenant_id=t.id),
                (SELECT COUNT(*) FROM institution_departments d WHERE d.tenant_id=t.id),
                (SELECT COUNT(*) FROM institution_buildings b WHERE b.tenant_id=t.id),
                (SELECT COUNT(*) FROM tenant_memberships m WHERE m.tenant_id=t.id AND m.is_active=1)
            FROM tenants t LEFT JOIN institution_profiles p ON p.tenant_id=t.id
            WHERE t.id=@tenant AND t.is_active=1
            """,db);
        Add(command,("@tenant",tenantId.ToString("D")));
        string name,kind,stage;
        int campuses,departments,buildings,members;
        await using (var reader=await command.ExecuteReaderAsync(ct))
        {
            if(!await reader.ReadAsync(ct))throw new InvalidOperationException("Institution inactive.");
            name=reader.GetString(0);
            kind=reader.GetString(1);
            stage=reader.GetString(2);
            campuses=Convert.ToInt32(reader.GetInt64(3));
            departments=Convert.ToInt32(reader.GetInt64(4));
            buildings=Convert.ToInt32(reader.GetInt64(5));
            members=Convert.ToInt32(reader.GetInt64(6));
        }
        var subscription=await SubscriptionAsync(tenantId,ct);
        return new InstitutionDashboard(tenantId,name,kind,stage,
            campuses,departments,buildings,members,subscription);
    }
}
