using System.Security.Cryptography;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

public sealed partial class MySqlStudentStore
{
    public async Task<DocumentRecord?> SaveDocumentAsync(Guid tenant,Guid campus,Guid student,
        string category,string filename,string mime,byte[] data,CancellationToken ct)
    {
        await using var db=Connect();await db.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        if(!await ActiveStudent(db,tx,tenant,campus,student,ct))return null;
        var id=Guid.NewGuid();
        await using var cmd=new MySqlCommand("""
            INSERT INTO student_documents
            (id,tenant_id,campus_id,student_id,category,original_filename,content_type,
             content_sha256,content_bytes,byte_length)
            VALUES(@id,@tenant,@campus,@student,@category,@filename,@mime,@hash,@bytes,@length)
            """,db,tx);
        Add(cmd,("@id",Id(id)),("@tenant",Id(tenant)),("@campus",Id(campus)),
            ("@student",Id(student)),("@category",category),("@filename",filename),
            ("@mime",mime),("@hash",SHA256.HashData(data)),("@bytes",data),("@length",data.Length));
        await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
        return new DocumentRecord(id,student,category,filename,mime,data.Length,"PENDING");
    }

    public async Task<IReadOnlyList<DocumentRecord>> DocumentsAsync(Guid tenant,Guid campus,Guid student,CancellationToken ct)
    {
        var list=new List<DocumentRecord>();
        await using var db=Connect();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT d.id,d.student_id,d.category,d.original_filename,d.content_type,
                   d.byte_length,d.verification_status
            FROM student_documents d
            JOIN students s ON s.id=d.student_id AND s.tenant_id=d.tenant_id AND s.campus_id=d.campus_id
            WHERE d.tenant_id=@tenant AND d.campus_id=@campus AND d.student_id=@student
            ORDER BY d.created_utc DESC LIMIT 100
            """,db);
        Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)),("@student",Id(student)));
        await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct))
            list.Add(new DocumentRecord(r.GetGuid(0),r.GetGuid(1),r.GetString(2),
                r.GetString(3),r.GetString(4),r.GetInt32(5),r.GetString(6)));
        return list;
    }

    public async Task<(string Name,string Mime,byte[] Bytes)?> ReadDocumentAsync(
        Guid tenant,Guid campus,Guid student,Guid document,CancellationToken ct)
    {
        await using var db=Connect();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT d.original_filename,d.content_type,d.content_bytes,d.content_sha256
            FROM student_documents d JOIN students s
              ON s.id=d.student_id AND s.tenant_id=d.tenant_id AND s.campus_id=d.campus_id
            WHERE d.tenant_id=@tenant AND d.campus_id=@campus AND d.student_id=@student AND d.id=@doc
            """,db);
        Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)),
            ("@student",Id(student)),("@doc",Id(document)));
        await using var r=await cmd.ExecuteReaderAsync(ct);
        if(!await r.ReadAsync(ct))return null;
        var bytes=(byte[])r.GetValue(2);
        var hash=(byte[])r.GetValue(3);
        if(!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes),hash))
            throw new InvalidOperationException("Stored document integrity verification failed.");
        return (r.GetString(0),r.GetString(1),bytes);
    }

    public async Task<bool> ReviewDocumentAsync(Guid tenant,Guid campus,Guid student,
        Guid document,string decision,Guid actor,CancellationToken ct)
    {
        await using var db=Connect();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            UPDATE student_documents SET verification_status=@decision,verified_by=@actor,
              verified_utc=UTC_TIMESTAMP(6)
            WHERE tenant_id=@tenant AND campus_id=@campus AND student_id=@student
              AND id=@doc AND verification_status='PENDING'
            """,db);
        Add(cmd,("@decision",decision),("@actor",Id(actor)),("@tenant",Id(tenant)),
            ("@campus",Id(campus)),("@student",Id(student)),("@doc",Id(document)));
        return await cmd.ExecuteNonQueryAsync(ct)==1;
    }
}
