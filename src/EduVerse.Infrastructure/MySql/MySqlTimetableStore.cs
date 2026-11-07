using EduVerse.Application.Attendance;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

public sealed partial class MySqlAttendanceStore
{
 public async Task<TimetableRecord?> CreateSlotAsync(Guid tenant,TimetableCreate req,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  // Tenant-wide lock serializes allocations of teachers shared across campuses.
  // Always acquire tenant before campus to keep lock ordering deterministic.
  await using(var tenantLock=new MySqlCommand("""
   SELECT id FROM tenants WHERE id=@tenant AND is_active=1 FOR UPDATE
   """,db,tx))
  {
   Add(tenantLock,("@tenant",Id(tenant)));
   if(await tenantLock.ExecuteScalarAsync(ct) is null)return null;
  }
  if(!await ActiveCampus(db,tx,tenant,req.CampusId,ct))return null;
  await LockCampus(db,tx,tenant,req.CampusId,ct);
  await using(var check=new MySqlCommand("""
   SELECT EXISTS(
    SELECT 1 FROM academic_years y
    JOIN academic_catalog c ON c.tenant_id=y.tenant_id AND c.campus_id=y.campus_id
       AND c.id=@class AND c.kind IN ('GRADE','CLASS','SECTION','COURSE','BATCH') AND c.is_active=1
    JOIN academic_catalog sub ON sub.tenant_id=y.tenant_id AND sub.campus_id=y.campus_id
       AND sub.id=@subject AND sub.kind='SUBJECT' AND sub.is_active=1
    JOIN academic_periods p ON p.tenant_id=y.tenant_id AND p.campus_id=y.campus_id
       AND p.id=@period AND p.is_active=1
    JOIN tenant_memberships m ON m.tenant_id=y.tenant_id AND m.subject_id=@teacher AND m.is_active=1
    JOIN identity_users u ON u.id=@teacher AND u.is_active=1
    JOIN academic_faculty_assignments a ON a.tenant_id=y.tenant_id
       AND a.campus_id=y.campus_id AND a.academic_year_id=y.id
       AND a.subject_id=sub.id AND a.faculty_subject_id=@teacher AND a.is_active=1
    WHERE y.tenant_id=@tenant AND y.campus_id=@campus AND y.id=@year AND y.is_active=1)
   """,db,tx))
  {
   Add(check,("@tenant",Id(tenant)),("@campus",Id(req.CampusId)),
    ("@year",Id(req.AcademicYearId)),("@class",Id(req.ClassItemId)),
    ("@subject",Id(req.SubjectId)),("@period",Id(req.PeriodId)),("@teacher",Id(req.TeacherId)));
   if(Convert.ToInt32(await check.ExecuteScalarAsync(ct))!=1)return null;
  }
  // Detect overlaps by time range, not merely identical period ID.
  await using(var conflict=new MySqlCommand("""
   SELECT EXISTS(SELECT 1 FROM academic_timetable_slots s
     JOIN academic_periods oldp ON oldp.id=s.period_id
     JOIN academic_periods newp ON newp.id=@period AND newp.tenant_id=@tenant AND newp.campus_id=@campus
     JOIN academic_years oldy ON oldy.id=s.academic_year_id
     JOIN academic_years newy ON newy.id=@year AND newy.tenant_id=@tenant AND newy.campus_id=@campus
     WHERE s.tenant_id=@tenant AND s.day_of_week=@day AND s.is_active=1
       AND oldy.starts_on<=newy.ends_on AND newy.starts_on<=oldy.ends_on
       AND oldp.starts_at<newp.ends_at AND newp.starts_at<oldp.ends_at
       AND (s.teacher_subject_id=@teacher OR
            (s.campus_id=@campus AND s.academic_year_id=@year AND s.class_item_id=@class)))
   """,db,tx))
  {
   Add(conflict,("@tenant",Id(tenant)),("@campus",Id(req.CampusId)),("@year",Id(req.AcademicYearId)),
    ("@period",Id(req.PeriodId)),("@day",req.DayOfWeek),
    ("@teacher",Id(req.TeacherId)),("@class",Id(req.ClassItemId)));
   if(Convert.ToInt32(await conflict.ExecuteScalarAsync(ct))==1)throw new TimetableConflictException();
  }
  var id=Guid.NewGuid();
  await using var cmd=new MySqlCommand("""
   INSERT INTO academic_timetable_slots(id,tenant_id,campus_id,academic_year_id,
    class_item_id,subject_id,period_id,teacher_subject_id,day_of_week)
   VALUES(@id,@tenant,@campus,@year,@class,@subject,@period,@teacher,@day)
   """,db,tx);
  Add(cmd,("@id",Id(id)),("@tenant",Id(tenant)),("@campus",Id(req.CampusId)),
   ("@year",Id(req.AcademicYearId)),("@class",Id(req.ClassItemId)),
   ("@subject",Id(req.SubjectId)),("@period",Id(req.PeriodId)),
   ("@teacher",Id(req.TeacherId)),("@day",req.DayOfWeek));
  await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
  return new TimetableRecord(id,req.CampusId,req.AcademicYearId,req.ClassItemId,
   req.SubjectId,req.PeriodId,req.TeacherId,req.DayOfWeek);
 }
 public async Task<IReadOnlyList<TimetableRecord>> SlotsAsync(Guid tenant,Guid campus,Guid year,CancellationToken ct)
 {
  var items=new List<TimetableRecord>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT id,campus_id,academic_year_id,class_item_id,subject_id,period_id,teacher_subject_id,day_of_week
   FROM academic_timetable_slots WHERE tenant_id=@tenant AND campus_id=@campus
    AND academic_year_id=@year AND is_active=1 ORDER BY day_of_week,period_id LIMIT 500
   """,db);
  Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)),("@year",Id(year)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))
   items.Add(new TimetableRecord(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),
    r.GetGuid(4),r.GetGuid(5),Guid.Parse(r.GetString(6)),r.GetInt32(7)));
  return items;
 }
}
