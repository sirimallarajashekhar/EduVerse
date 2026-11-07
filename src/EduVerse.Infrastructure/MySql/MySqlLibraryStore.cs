using EduVerse.Application.OperationsModules;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlOperationsStore
{
 private static LibraryTitle MapTitle(MySqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3),r.IsDBNull(4)?null:r.GetString(4));
 private static LibraryCopy MapCopy(MySqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3));
 private static LibraryLoan MapLoan(MySqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),
   DateOnly.FromDateTime(r.GetDateTime(3)),DateOnly.FromDateTime(r.GetDateTime(4)),
   r.IsDBNull(5)?null:DateOnly.FromDateTime(r.GetDateTime(5)),r.GetDecimal(6),r.GetString(7));
 public async Task<LibraryTitle?> Title(Guid tenant,LibraryTitleCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Campus(db,tx,tenant,r.CampusId,ct))return null;
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO library_titles(id,tenant_id,campus_id,isbn,title,author)
   VALUES(@id,@t,@c,@isbn,@title,@author)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
   ("@isbn",r.Isbn),("@title",r.Title),("@author",r.Author));
  await tx.CommitAsync(ct);
  return new LibraryTitle(id,r.CampusId,r.Title,r.Author,r.Isbn);
 }
 public async Task<IReadOnlyList<LibraryTitle>> Titles(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,campus_id,title,author,isbn FROM library_titles
   WHERE tenant_id=@t AND campus_id=@c ORDER BY title LIMIT 300
   """,MapTitle,ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<LibraryCopy?> Copy(Guid tenant,LibraryCopyCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Exists(db,tx,"SELECT EXISTS(SELECT 1 FROM library_titles WHERE id=@id AND tenant_id=@t AND campus_id=@c)",
   ct,("@id",Id(r.TitleId)),("@t",Id(tenant)),("@c",Id(r.CampusId))))return null;
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO library_copies(id,tenant_id,campus_id,title_id,barcode)
   VALUES(@id,@t,@c,@title,@barcode)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
    ("@title",Id(r.TitleId)),("@barcode",r.Barcode));
  await tx.CommitAsync(ct);return new LibraryCopy(id,r.TitleId,r.Barcode,"AVAILABLE");
 }
 public async Task<IReadOnlyList<LibraryCopy>> Copies(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,title_id,barcode,status FROM library_copies
   WHERE tenant_id=@t AND campus_id=@c ORDER BY barcode LIMIT 500
   """,MapCopy,ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<LibraryLoan?> Borrow(Guid tenant,LibraryLoanCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  if(!await Student(db,tx,tenant,r.CampusId,r.StudentId,ct))return null;
  Guid title;
  await using(var cmd=new MySqlCommand("""
   SELECT title_id FROM library_copies
   WHERE id=@copy AND tenant_id=@t AND campus_id=@c AND status='AVAILABLE' FOR UPDATE
   """,db,tx))
  {
   Add(cmd,("@copy",Id(r.CopyId)),("@t",Id(tenant)),("@c",Id(r.CampusId)));
   var v=await cmd.ExecuteScalarAsync(ct);
   if(v is null)throw new OperationsConflictException("Copy is not currently available.");
   title=v is Guid g?g:Guid.Parse(Convert.ToString(v)!);
  }
  var firstWait=await Scalar(db,tx,"""
   SELECT student_id FROM library_reservations WHERE tenant_id=@t AND campus_id=@c
    AND title_id=@title AND status='WAITING' ORDER BY created_utc,id LIMIT 1
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@title",Id(title)));
  if(firstWait is not null&&Convert.ToString(firstWait)!=Id(r.StudentId))
   throw new OperationsConflictException("Copy is held for an earlier reservation.");
  if(await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM library_loans WHERE tenant_id=@t AND campus_id=@c
    AND student_id=@s AND copy_id=@copy AND status='OPEN')
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),
    ("@s",Id(r.StudentId)),("@copy",Id(r.CopyId))))
   throw new OperationsConflictException("The student already has this copy.");
  var today=DateOnly.FromDateTime(DateTime.UtcNow);
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO library_loans(id,tenant_id,campus_id,copy_id,student_id,borrowed_on,due_on)
   VALUES(@id,@t,@c,@copy,@student,@date,@due)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
    ("@copy",Id(r.CopyId)),("@student",Id(r.StudentId)),("@date",Day(today)),("@due",Day(r.DueOn)));
  await Exec(db,tx,"UPDATE library_copies SET status='LENT' WHERE tenant_id=@t AND campus_id=@c AND id=@copy",
   ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@copy",Id(r.CopyId)));
  if(firstWait is not null)
   await Exec(db,tx,"""
    UPDATE library_reservations SET status='FULFILLED' WHERE tenant_id=@t
    AND campus_id=@c AND title_id=@title AND student_id=@student AND status='WAITING'
    """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@title",Id(title)),("@student",Id(r.StudentId)));
  await tx.CommitAsync(ct);
  return new LibraryLoan(id,r.CopyId,r.StudentId,today,r.DueOn,null,0,"OPEN");
 }
 public async Task<LibraryLoan?> Return(Guid tenant,Guid campus,Guid loan,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  var rows=await Query(db,tx,"""
   SELECT id,copy_id,student_id,borrowed_on,due_on,returned_on,fine_amount,status
   FROM library_loans WHERE tenant_id=@t AND campus_id=@c AND id=@id AND status='OPEN' FOR UPDATE
   """,MapLoan,ct,("@t",Id(tenant)),("@c",Id(campus)),("@id",Id(loan)));
  if(rows.Count==0)return null;
  var old=rows[0];var today=DateOnly.FromDateTime(DateTime.UtcNow);
  var overdue=Math.Max(0,today.DayNumber-old.DueOn.DayNumber);
  var fine=Math.Min(500m,overdue*2m);
  await Exec(db,tx,"""
   UPDATE library_loans SET status='RETURNED',returned_on=@date,fine_amount=@fine
   WHERE tenant_id=@t AND campus_id=@c AND id=@id AND status='OPEN'
   """,ct,("@date",Day(today)),("@fine",fine),("@t",Id(tenant)),("@c",Id(campus)),("@id",Id(loan)));
  await Exec(db,tx,"UPDATE library_copies SET status='AVAILABLE' WHERE tenant_id=@t AND campus_id=@c AND id=@id",
    ct,("@t",Id(tenant)),("@c",Id(campus)),("@id",Id(old.CopyId)));
  await tx.CommitAsync(ct);
  return old with {ReturnedOn=today,Fine=fine,Status="RETURNED"};
 }
 public async Task<IReadOnlyList<LibraryLoan>> Loans(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,copy_id,student_id,borrowed_on,due_on,returned_on,fine_amount,status
   FROM library_loans WHERE tenant_id=@t AND campus_id=@c ORDER BY created_utc DESC LIMIT 500
   """,MapLoan,ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<LibraryReservation?> Reserve(Guid tenant,LibraryReservationCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  if(!await Student(db,tx,tenant,r.CampusId,r.StudentId,ct)||!await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM library_titles WHERE id=@title AND tenant_id=@t AND campus_id=@c)
   """,ct,("@title",Id(r.TitleId)),("@t",Id(tenant)),("@c",Id(r.CampusId))))return null;
  if(await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM library_reservations WHERE tenant_id=@t AND campus_id=@c
    AND title_id=@title AND student_id=@student AND status='WAITING')
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@title",Id(r.TitleId)),("@student",Id(r.StudentId))))
   throw new OperationsConflictException("Student already has a waiting reservation.");
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO library_reservations(id,tenant_id,campus_id,title_id,student_id)
   VALUES(@id,@t,@c,@title,@student)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
    ("@title",Id(r.TitleId)),("@student",Id(r.StudentId)));
  await tx.CommitAsync(ct);
  return new LibraryReservation(id,r.TitleId,r.StudentId,"WAITING");
 }
 public async Task<IReadOnlyList<LibraryReservation>> Reservations(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,title_id,student_id,status FROM library_reservations
   WHERE tenant_id=@t AND campus_id=@c ORDER BY created_utc DESC LIMIT 250
   """,r=>new LibraryReservation(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetString(3)),
   ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
}
