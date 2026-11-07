using Microsoft.AspNetCore.Identity;
using MySqlConnector;

// One-time operator-only tool. NEVER expose this operation as an anonymous HTTP endpoint.
// Use a dedicated provisioning SQL account and supply passwords via secret/environment management.
var connectionString = Environment.GetEnvironmentVariable("EDUVERSE_BOOTSTRAP_MYSQL");
var slug = Environment.GetEnvironmentVariable("EDUVERSE_BOOTSTRAP_TENANT");
var institution = Environment.GetEnvironmentVariable("EDUVERSE_BOOTSTRAP_NAME");
var kind = Environment.GetEnvironmentVariable("EDUVERSE_BOOTSTRAP_KIND");
var email = Environment.GetEnvironmentVariable("EDUVERSE_BOOTSTRAP_EMAIL");
var password = Environment.GetEnvironmentVariable("EDUVERSE_BOOTSTRAP_PASSWORD");

if (string.IsNullOrWhiteSpace(connectionString) ||
    string.IsNullOrWhiteSpace(slug) ||
    string.IsNullOrWhiteSpace(institution) ||
    string.IsNullOrWhiteSpace(email) ||
    string.IsNullOrWhiteSpace(password) ||
    kind is not ("School" or "College" or "Mixed"))
{
    Console.Error.WriteLine("Missing required EDUVERSE_BOOTSTRAP_* environment configuration.");
    return 1;
}
if (slug.Length is < 3 or > 80 || !slug.All(c => char.IsAsciiLetterLower(c) || char.IsDigit(c) || c=='-') ||
    email.Length > 254 || !email.Contains('@') || institution.Length > 160 ||
    password.Length is < 12 or > 128 || !password.Any(char.IsUpper) ||
    !password.Any(char.IsLower) || !password.Any(char.IsDigit) ||
    !password.Any(c => !char.IsLetterOrDigit(c)))
{
    Console.Error.WriteLine("Invalid input. Password requires >=12 mixed characters with a symbol.");
    return 1;
}
var tenantId = Guid.NewGuid();
var userId = Guid.NewGuid();
var passwordHash = new PasswordHasher<object>().HashPassword(new object(), password);
await using var db = new MySqlConnection(connectionString);
await db.OpenAsync();
await using var transaction = await db.BeginTransactionAsync();
try
{
    await using (var cmd = new MySqlCommand("""
        INSERT INTO tenants(id,slug,display_name,kind,is_active)
        VALUES (@id,@slug,@name,@kind,1)
        """, db, transaction))
    {
        cmd.Parameters.AddWithValue("@id", tenantId.ToString("D"));
        cmd.Parameters.AddWithValue("@slug", slug);
        cmd.Parameters.AddWithValue("@name", institution);
        cmd.Parameters.AddWithValue("@kind", kind);
        await cmd.ExecuteNonQueryAsync();
    }
    await using (var cmd = new MySqlCommand("""
        INSERT INTO identity_users(id,email,email_normalized,password_hash,display_name,is_active)
        VALUES (@id,@email,@normalized,@hash,@name,1)
        """, db, transaction))
    {
        cmd.Parameters.AddWithValue("@id", userId.ToString("D"));
        cmd.Parameters.AddWithValue("@email", email);
        cmd.Parameters.AddWithValue("@normalized", email.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@hash", passwordHash);
        cmd.Parameters.AddWithValue("@name", "Institution Administrator");
        await cmd.ExecuteNonQueryAsync();
    }
    await using (var cmd = new MySqlCommand("""
        INSERT INTO tenant_memberships(tenant_id,subject_id,role_code,is_active)
        VALUES (@tenant,@subject,'ADMIN',1)
        """, db, transaction))
    {
        cmd.Parameters.AddWithValue("@tenant", tenantId.ToString("D"));
        cmd.Parameters.AddWithValue("@subject", userId.ToString("D"));
        await cmd.ExecuteNonQueryAsync();
    }
    await transaction.CommitAsync();
    Console.WriteLine($"Created institution {slug} ({tenantId:D}) and initial admin {email}.");
    return 0;
}
catch (MySqlException ex) when (ex.Number == 1062)
{
    await transaction.RollbackAsync();
    Console.Error.WriteLine("Institution or email already exists. No changes were committed.");
    return 2;
}
