using EduVerse.Application.Attendance;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

public sealed partial class MySqlAttendanceStore
{
 public async Task<StaffAttendanceRecord?> MarkStaffAsync(Guid tenant,Guid actor,StaffAttendanceMark req,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  if(!await ActiveCampus(db,tx,tenant,req.CampusId,ct))return null;
  await LockCampus(db,tx,tenant,req.CampusId,ct);
  if(await Holiday(db,tx,tenant,req.CampusId,req.Date,ct))throw new AttendanceHolidayException();
  await using(var member=new MySqlCommand("""
   SELECT EXISTS(SELECT 1 FROM tenant_memberships m
     JOIN identity_users u ON u.id=m.subject_id AND u.is_active=1
     WHERE m.tenant_id=@tenant AND m.subject_id=@staff AND m.is_active=1)
   """,db,tx))
  {
   Add(member,("@tenant",Id(tenant)),("@staff",Id(req.StaffUserId)));
   if(Convert.ToInt32(await member.ExecuteScalarAsync(ct))!=1)return null;
  }
  var id=Guid.NewGuid();
  await using(var cmd=new MySqlCommand("""
   INSERT INTO staff_attendance
    (id,tenant_id,campus_id,staff_subject_id,attendance_date,status,remarks,marked_by)
   VALUES(@id,@tenant,@campus,@staff,@date,@status,@remarks,@actor)
   ON DUPLICATE KEY UPDATE status=VALUES(status),remarks=VALUES(remarks),
     marked_by=VALUES(marked_by),marked_utc=UTC_TIMESTAMP(6),id=id
   """,db,tx))
  {
   Add(cmd,("@id",Id(id)),("@tenant",Id(tenant)),("@campus",Id(req.CampusId)),
    ("@staff",Id(req.StaffUserId)),("@date",Date(req.Date)),("@status",req.Status),
    ("@remarks",req.Remarks),("@actor",Id(actor)));
   await cmd.ExecuteNonQueryAsync(ct);
  }
  await using(var query=new MySqlCommand("""
   SELECT id FROM staff_attendance WHERE tenant_id=@tenant AND campus_id=@campus
    AND staff_subject_id=@staff AND attendance_date=@date
   """,db,tx))
  {
   Add(query,("@tenant",Id(tenant)),("@campus",Id(req.CampusId)),
    ("@staff",Id(req.StaffUserId)),("@date",Date(req.Date)));
   var actual=await query.ExecuteScalarAsync(ct);
   id=actual is Guid g?g:Guid.Parse(Convert.ToString(actual)!);
  }
  await tx.CommitAsync(ct);
  return new StaffAttendanceRecord(id,req.CampusId,req.StaffUserId,req.Date,req.Status);
 }
 public async Task<IReadOnlyList<StaffAttendanceRecord>> StaffMarksAsync(Guid tenant,Guid campus,DateOnly date,CancellationToken ct)
 {
  var result=new List<StaffAttendanceRecord>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT a.id,a.campus_id,a.staff_subject_id,a.attendance_date,a.status
   FROM staff_attendance a JOIN tenant_memberships m
    ON m.tenant_id=a.tenant_id AND m.subject_id=a.staff_subject_id
   WHERE a.tenant_id=@tenant AND a.campus_id=@campus AND a.attendance_date=@date
   ORDER BY a.staff_subject_id LIMIT 500
   """,db);
  Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)),("@date",Date(date)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))
   result.Add(new StaffAttendanceRecord(r.GetGuid(0),r.GetGuid(1),Guid.Parse(r.GetString(2)),
    DateOnly.FromDateTime(r.GetDateTime(3)),r.GetString(4)));
  return result;
 }
 public async Task<AttendanceDashboard> DashboardAsync(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT
    (SELECT COUNT(*) FROM academic_periods WHERE tenant_id=@tenant AND campus_id=@campus),
    (SELECT COUNT(*) FROM academic_timetable_slots WHERE tenant_id=@tenant AND campus_id=@campus),
    (SELECT COUNT(*) FROM student_attendance WHERE tenant_id=@tenant AND campus_id=@campus),
    (SELECT COUNT(*) FROM staff_attendance WHERE tenant_id=@tenant AND campus_id=@campus)
   """,db);
  Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)));
  await using var r=await cmd.ExecuteReaderAsync(ct);await r.ReadAsync(ct);
  return new AttendanceDashboard(Convert.ToInt32(r.GetInt64(0)),Convert.ToInt32(r.GetInt64(1)),
    Convert.ToInt32(r.GetInt64(2)),Convert.ToInt32(r.GetInt64(3)));
 }
}
