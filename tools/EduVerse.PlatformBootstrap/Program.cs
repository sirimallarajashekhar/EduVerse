using Microsoft.AspNetCore.Identity;
using MySqlConnector;

var connection=Environment.GetEnvironmentVariable("ConnectionStrings__EduVerse");
if(string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException("ConnectionStrings__EduVerse environment variable required.");
if(Console.IsInputRedirected || Console.IsOutputRedirected)
    throw new InvalidOperationException("Run from an interactive terminal.");
Console.Write("Verified platform operator email: ");
var email=Console.ReadLine()?.Trim();
Console.Write("Display name: ");
var name=Console.ReadLine()?.Trim();
if(string.IsNullOrWhiteSpace(email) || email.Length>254 || !email.Contains('@') ||
   string.IsNullOrWhiteSpace(name) || name.Length>160)
    throw new ArgumentException("Invalid email or name.");
Console.Write("New operator password (hidden): ");
var password=ReadSecret();
Console.Write("Confirm password (hidden): ");
var confirm=ReadSecret();
if(password!=confirm || password.Length<16 || password.Length>128 ||
   !password.Any(char.IsUpper)||!password.Any(char.IsLower)||!password.Any(char.IsDigit)||
   !password.Any(c=>!char.IsLetterOrDigit(c)))
    throw new ArgumentException("Password mismatch or weak password: 16+ chars, upper/lower/digit/symbol.");
Console.Write("Type BOOTSTRAP to provision operator: ");
if(Console.ReadLine()!="BOOTSTRAP")return;
var hash=new PasswordHasher<object>().HashPassword(new object(),password);
var id=Guid.NewGuid();
await using var db=new MySqlConnection(connection);
await db.OpenAsync();
await using var tx=await db.BeginTransactionAsync();
await using(var existing=new MySqlCommand(
    "SELECT COUNT(*) FROM identity_users WHERE email_normalized=@email",db,tx))
{
    existing.Parameters.AddWithValue("@email",email.ToUpperInvariant());
    if(Convert.ToInt32(await existing.ExecuteScalarAsync())!=0)
        throw new InvalidOperationException("This email already exists. Grant the existing verified account out-of-band instead.");
}
await using(var insert=new MySqlCommand("""
    INSERT INTO identity_users(id,email,email_normalized,password_hash,display_name)
    VALUES(@id,@email,@normalized,@hash,@name)
    """,db,tx))
{
    insert.Parameters.AddWithValue("@id",id.ToString("D"));
    insert.Parameters.AddWithValue("@email",email);
    insert.Parameters.AddWithValue("@normalized",email.ToUpperInvariant());
    insert.Parameters.AddWithValue("@hash",hash);
    insert.Parameters.AddWithValue("@name",name);
    await insert.ExecuteNonQueryAsync();
}
await using(var grant=new MySqlCommand(
    "INSERT INTO platform_operators(user_id) VALUES(@id)",db,tx))
{
    grant.Parameters.AddWithValue("@id",id.ToString("D"));
    await grant.ExecuteNonQueryAsync();
}
await tx.CommitAsync();
Console.WriteLine("Platform operator provisioned. Sign in at /platform/login.");
static string ReadSecret()
{
    var chars=new List<char>();
    while(true)
    {
        var key=Console.ReadKey(intercept:true);
        if(key.Key==ConsoleKey.Enter){Console.WriteLine();return new string(chars.ToArray());}
        if(key.Key==ConsoleKey.Backspace){if(chars.Count>0)chars.RemoveAt(chars.Count-1);continue;}
        if(!char.IsControl(key.KeyChar) && chars.Count<128)chars.Add(key.KeyChar);
    }
}
