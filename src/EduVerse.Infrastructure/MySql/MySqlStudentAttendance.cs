using EduVerse.Application.Attendance;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

public sealed partial class MySqlAttendanceStore
{
 public async Task<AttendanceRecord?> MarkStudentAsync(Guid tenant,Guid actor,StudentAttendanceMark req,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  if(!await ActiveCampus(db,tx,tenant,req.CampusId,ct))return null;
  await LockCampus(db,tx,tenant,req.CampusId,ct);
  if(await Holiday(db,tx,tenant,req.CampusId,req.Date,ct))throw new AttendanceHolidayException();

  Guid student;
  Guid item;
  Guid year;
  await using(var check=new MySqlCommand("""
   SELECT e.student_id,e.academic_item_id,e.academic_year_id
   FROM student_enrollments e
   JOIN students s ON s.id=e.student_id AND s.tenant_id=e.tenant_id AND s.campus_id=e.campus_id
   JOIN academic_years y ON y.id=e.academic_year_id AND y.tenant_id=e.tenant_id AND y.campus_id=e.campus_id
   WHERE e.id=@enrollment AND e.tenant_id=@tenant AND e.campus_id=@campus
      AND e.status='ACTIVE' AND s.status='ACTIVE' AND y.is_active=1
      AND y.starts_on<=@date AND y.ends_on>=@date
   FOR UPDATE
   """,db,tx))
  {
   Add(check,("@enrollment",Id(req.EnrollmentId)),("@tenant",Id(tenant)),
    ("@campus",Id(req.CampusId)),("@date",Date(req.Date)));
   await using var reader=await check.ExecuteReaderAsync(ct);
   if(!await reader.ReadAsync(ct))return null;
   student=reader.GetGuid(0);item=reader.GetGuid(1);year=reader.GetGuid(2);
  }
  if(req.PeriodId is Guid period)
  {
   // Period attendance requires the exact class+year+weekday to have a scheduled slot.
   // Monday=1, ..., Sunday=7.
   var weekday=req.Date.DayOfWeek==DayOfWeek.Sunday?7:(int)req.Date.DayOfWeek;
   await using var check=new MySqlCommand("""
    SELECT EXISTS(SELECT 1 FROM academic_timetable_slots t
      JOIN academic_periods p ON p.tenant_id=t.tenant_id AND p.campus_id=t.campus_id
       AND p.id=t.period_id AND p.is_active=1
      WHERE t.tenant_id=@tenant AND t.campus_id=@campus AND t.class_item_id=@item
       AND t.academic_year_id=@year AND t.period_id=@period
       AND t.day_of_week=@weekday AND t.is_active=1)
    """,db,tx);
   Add(check,("@tenant",Id(tenant)),("@campus",Id(req.CampusId)),("@item",Id(item)),
    ("@year",Id(year)),("@period",Id(period)),("@weekday",weekday));
   if(Convert.ToInt32(await check.ExecuteScalarAsync(ct))!=1)return null;
  }
  var key=req.PeriodId is Guid p?Id(p):"DAILY";
  var scope=req.PeriodId is null?"DAILY":"PERIOD";
  var id=Guid.NewGuid();
  await using(var cmd=new MySqlCommand("""
   INSERT INTO student_attendance(id,tenant_id,campus_id,student_id,enrollment_id,attendance_date,
       period_id,attendance_scope,attendance_key,status,remarks,marked_by)
   VALUES(@id,@tenant,@campus,@student,@enrollment,@date,@period,@scope,@key,@status,@remarks,@actor)
   ON DUPLICATE KEY UPDATE status=VALUES(status),remarks=VALUES(remarks),
       marked_by=VALUES(marked_by),marked_utc=UTC_TIMESTAMP(6),id=id
   """,db,tx))
  {
   Add(cmd,("@id",Id(id)),("@tenant",Id(tenant)),("@campus",Id(req.CampusId)),
    ("@student",Id(student)),("@enrollment",Id(req.EnrollmentId)),
    ("@date",Date(req.Date)),("@period",req.PeriodId?.ToString("D")),
    ("@scope",scope),("@key",key),("@status",req.Status),("@remarks",req.Remarks),
    ("@actor",Id(actor)));
   await cmd.ExecuteNonQueryAsync(ct);
  }
  await using(var retrieve=new MySqlCommand("""
   SELECT id FROM student_attendance WHERE tenant_id=@tenant AND campus_id=@campus
      AND student_id=@student AND attendance_date=@date AND attendance_key=@key
   """,db,tx))
  {
   Add(retrieve,("@tenant",Id(tenant)),("@campus",Id(req.CampusId)),
    ("@student",Id(student)),("@date",Date(req.Date)),("@key",key));
   var actual=await retrieve.ExecuteScalarAsync(ct);
   id=actual is Guid g?g:Guid.Parse(Convert.ToString(actual)!);
  }
  await tx.CommitAsync(ct);
  return new AttendanceRecord(id,req.CampusId,student,req.EnrollmentId,req.Date,req.PeriodId,scope,req.Status);
 }
 public async Task<IReadOnlyList<AttendanceRecord>> StudentMarksAsync(Guid tenant,Guid campus,Guid enrollment,DateOnly? date,CancellationToken ct)
 {
  var items=new List<AttendanceRecord>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT a.id,a.campus_id,a.student_id,a.enrollment_id,a.attendance_date,a.period_id,
    a.attendance_scope,a.status
   FROM student_attendance a JOIN student_enrollments e
    ON e.id=a.enrollment_id AND e.tenant_id=a.tenant_id AND e.campus_id=a.campus_id
   WHERE a.tenant_id=@tenant AND a.campus_id=@campus AND a.enrollment_id=@enrollment
    AND (@date IS NULL OR a.attendance_date=@date)
   ORDER BY a.attendance_date DESC,a.attendance_key LIMIT 500
   """,db);
  Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)),
    ("@enrollment",Id(enrollment)),("@date",date is null?null:Date(date.Value)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))
   items.Add(new AttendanceRecord(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),
    DateOnly.FromDateTime(r.GetDateTime(4)),r.IsDBNull(5)?null:r.GetGuid(5),
    r.GetString(6),r.GetString(7)));
  return items;
 }
 public async Task<AttendanceSummary> SummaryAsync(Guid tenant,Guid campus,Guid enrollment,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT COUNT(*),COALESCE(SUM(a.status='PRESENT'),0),COALESCE(SUM(a.status='ABSENT'),0),
    COALESCE(SUM(a.status='LATE'),0),COALESCE(SUM(a.status='EXCUSED'),0)
   FROM student_attendance a JOIN student_enrollments e
     ON e.id=a.enrollment_id AND e.tenant_id=a.tenant_id AND e.campus_id=a.campus_id
   WHERE a.tenant_id=@tenant AND a.campus_id=@campus AND a.enrollment_id=@enrollment
    AND a.attendance_scope='DAILY'
   """,db);
  Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)),("@enrollment",Id(enrollment)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  await r.ReadAsync(ct);
  int total=Convert.ToInt32(r.GetInt64(0)),present=Convert.ToInt32(r.GetDecimal(1)),
   absent=Convert.ToInt32(r.GetDecimal(2)),late=Convert.ToInt32(r.GetDecimal(3)),
   excused=Convert.ToInt32(r.GetDecimal(4));
  return new AttendanceSummary(total,present,absent,late,excused,
   total==0?0:Math.Round(100m*(present+late+excused)/total,2));
 }
}
