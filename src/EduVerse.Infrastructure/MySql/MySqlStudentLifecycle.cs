using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

public sealed partial class MySqlStudentStore
{
    public async Task<bool> LifecycleAsync(Guid tenant,Guid campus,Guid student,LifecycleCommand request,Guid actor,CancellationToken ct)
    {
        await using var db=Connect();await db.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        // Lock the student to serialize competing lifecycle transitions.
        string? status;
        await using(var check=new MySqlCommand("""
            SELECT status FROM students
            WHERE tenant_id=@tenant AND campus_id=@campus AND id=@student FOR UPDATE
            """,db,tx))
        {
            Add(check,("@tenant",Id(tenant)),("@campus",Id(campus)),("@student",Id(student)));
            status=await check.ExecuteScalarAsync(ct) as string;
        }
        if(status!="ACTIVE")return false;
        Guid? formerItem=null;
        if(request.Action=="PROMOTE")
        {
            var placement=new EnrollmentCreate(request.AcademicYearId!.Value,request.AcademicItemId!.Value);
            if(!await ValidPlacement(db,tx,tenant,campus,placement,ct))return false;
            await using(var read=new MySqlCommand("""
                SELECT academic_item_id FROM student_enrollments
                WHERE tenant_id=@tenant AND campus_id=@campus AND student_id=@student AND status='ACTIVE'
                ORDER BY created_utc DESC LIMIT 1 FOR UPDATE
                """,db,tx))
            {
                Add(read,("@tenant",Id(tenant)),("@campus",Id(campus)),("@student",Id(student)));
                var raw=await read.ExecuteScalarAsync(ct);
                if(raw is null)return false;
                formerItem=raw is Guid g?g:Guid.Parse(Convert.ToString(raw)!);
            }
            await using(var old=new MySqlCommand("""
                UPDATE student_enrollments SET status='PROMOTED'
                WHERE tenant_id=@tenant AND campus_id=@campus AND student_id=@student AND status='ACTIVE'
                """,db,tx))
            {
                Add(old,("@tenant",Id(tenant)),("@campus",Id(campus)),("@student",Id(student)));
                await old.ExecuteNonQueryAsync(ct);
            }
            await using(var added=new MySqlCommand("""
                INSERT INTO student_enrollments
                  (id,tenant_id,campus_id,student_id,academic_year_id,academic_item_id)
                VALUES (@id,@tenant,@campus,@student,@year,@item)
                """,db,tx))
            {
                Add(added,("@id",Id(Guid.NewGuid())),("@tenant",Id(tenant)),("@campus",Id(campus)),
                    ("@student",Id(student)),("@year",Id(placement.AcademicYearId)),("@item",Id(placement.AcademicItemId)));
                await added.ExecuteNonQueryAsync(ct);
            }
        }
        else
        {
            if(request.Action=="TRANSFER")
            {
                if(request.TargetCampusId is not Guid destination||destination==campus||
                   !await ActiveCampus(db,tx,tenant,destination,ct))return false;
            }
            await using(var update=new MySqlCommand("""
                UPDATE students SET status=@status WHERE tenant_id=@tenant AND campus_id=@campus AND id=@student
                """,db,tx))
            {
                Add(update,("@status",request.Action=="TRANSFER"?"TRANSFERRED":"WITHDRAWN"),
                    ("@tenant",Id(tenant)),("@campus",Id(campus)),("@student",Id(student)));
                await update.ExecuteNonQueryAsync(ct);
            }
            await using(var enrollments=new MySqlCommand("""
                UPDATE student_enrollments SET status=@status
                WHERE tenant_id=@tenant AND campus_id=@campus AND student_id=@student AND status='ACTIVE'
                """,db,tx))
            {
                Add(enrollments,("@status",request.Action=="TRANSFER"?"TRANSFERRED":"WITHDRAWN"),
                    ("@tenant",Id(tenant)),("@campus",Id(campus)),("@student",Id(student)));
                await enrollments.ExecuteNonQueryAsync(ct);
            }
        }
        await using(var audit=new MySqlCommand("""
            INSERT INTO student_lifecycle_events(id,tenant_id,campus_id,student_id,event_code,
              reason,old_item_id,new_item_id,target_campus_id,actor_id)
            VALUES(@id,@tenant,@campus,@student,@event,@reason,@old,@new,@target,@actor)
            """,db,tx))
        {
            Add(audit,("@id",Id(Guid.NewGuid())),("@tenant",Id(tenant)),("@campus",Id(campus)),
                ("@student",Id(student)),("@event",request.Action=="PROMOTE"?"PROMOTED":request.Action=="TRANSFER"?"TRANSFERRED":"WITHDRAWN"),
                ("@reason",request.Reason),("@old",formerItem?.ToString("D")),
                ("@new",request.AcademicItemId?.ToString("D")),("@target",request.TargetCampusId?.ToString("D")),
                ("@actor",Id(actor)));
            await audit.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return true;
    }
}
