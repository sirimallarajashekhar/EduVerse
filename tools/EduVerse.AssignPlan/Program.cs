using MySqlConnector;

// Operator-only CLI: deliberately no user-facing API for billing/plan escalation.
var connection=Environment.GetEnvironmentVariable("EDUVERSE_OPERATOR_MYSQL");
var slug=Environment.GetEnvironmentVariable("EDUVERSE_OPERATOR_TENANT");
var plan=Environment.GetEnvironmentVariable("EDUVERSE_OPERATOR_PLAN");
if(string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(slug) ||
    plan is not ("PILOT" or "STANDARD"))
{
    Console.Error.WriteLine("Set EDUVERSE_OPERATOR_MYSQL, EDUVERSE_OPERATOR_TENANT and supported EDUVERSE_OPERATOR_PLAN.");
    return 1;
}
await using var db=new MySqlConnection(connection);
await db.OpenAsync();
await using var transaction=await db.BeginTransactionAsync();
await using var assign=new MySqlCommand("""
    INSERT INTO institution_subscriptions(tenant_id,plan_code,status,starts_utc,expires_utc)
    SELECT t.id,@plan,'ACTIVE',UTC_TIMESTAMP(6),NULL FROM tenants t
    WHERE t.slug=@slug AND t.is_active=1
    ON DUPLICATE KEY UPDATE plan_code=VALUES(plan_code),status='ACTIVE',
      starts_utc=UTC_TIMESTAMP(6),expires_utc=NULL
    """,db,transaction);
assign.Parameters.AddWithValue("@plan",plan);
assign.Parameters.AddWithValue("@slug",slug);
var changed=await assign.ExecuteNonQueryAsync();
if(changed<1)
{
    await transaction.RollbackAsync();
    Console.Error.WriteLine("Institution was not found or is inactive.");
    return 2;
}
await using var audit=new MySqlCommand("""
    INSERT INTO identity_audit_events(tenant_id,event_code)
    SELECT id,'SUBSCRIPTION_PLAN_ASSIGNED' FROM tenants WHERE slug=@slug
    """,db,transaction);
audit.Parameters.AddWithValue("@slug",slug);
await audit.ExecuteNonQueryAsync();
await transaction.CommitAsync();
Console.WriteLine("Subscription assigned to institution: "+slug+" ("+plan+").");
return 0;
