using System.Security.Cryptography;
using EduVerse.Contracts;
using MySqlConnector;
namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlHrmsStore
{
 public async Task<HrDocument?> Upload(Guid tenant,Guid campus,Guid employee,string type,string filename,
    string mime,byte[] content,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Employee(db,tx,tenant,campus,employee,ct))return null;
  var id=Guid.NewGuid();
  await using var cmd=new MySqlCommand("""
   INSERT INTO hr_employee_documents(id,tenant_id,campus_id,employee_id,document_type,file_name,
    mime_type,content_sha256,payload,byte_length)
   VALUES(@id,@t,@c,@employee,@type,@name,@mime,@hash,@data,@size)
   """,db,tx);
  Add(cmd,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(campus)),("@employee",Id(employee)),
    ("@type",type),("@name",filename),("@mime",mime),("@hash",SHA256.HashData(content)),
    ("@data",content),("@size",content.Length));
  await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
  return new HrDocument(id,employee,type,filename,content.Length);
 }
 public async Task<IReadOnlyList<HrDocument>> Documents(Guid tenant,Guid campus,Guid employee,CancellationToken ct)
 {
  var list=new List<HrDocument>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT id,employee_id,document_type,file_name,byte_length FROM hr_employee_documents
   WHERE tenant_id=@t AND campus_id=@c AND employee_id=@employee
   ORDER BY created_utc DESC LIMIT 100
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)),("@employee",Id(employee)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))list.Add(new HrDocument(r.GetGuid(0),r.GetGuid(1),
   r.GetString(2),r.GetString(3),r.GetInt32(4)));
  return list;
 }
 public async Task<(string FileName,byte[] Bytes)?> Download(Guid tenant,Guid campus,Guid employee,Guid document,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT file_name,payload,content_sha256 FROM hr_employee_documents
   WHERE tenant_id=@t AND campus_id=@c AND employee_id=@employee AND id=@document
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)),
   ("@employee",Id(employee)),("@document",Id(document)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  if(!await r.ReadAsync(ct))return null;
  var content=(byte[])r.GetValue(1);
  if(!CryptographicOperations.FixedTimeEquals(SHA256.HashData(content),(byte[])r.GetValue(2)))
   throw new InvalidOperationException("Employee document integrity mismatch.");
  return (r.GetString(0),content);
 }
}
