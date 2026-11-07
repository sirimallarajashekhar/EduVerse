using EduVerse.Application.Tenancy;
using EduVerse.Contracts;
using EduVerse.Domain;

namespace EduVerse.Application.Students;

/// <summary>Only the API performs student/admissions validation, sensitive reads, and role decisions.</summary>
public sealed class StudentService(IStudentStore store,ITenantContext context)
{
    private async Task<TenantIdentity> Admin(CancellationToken ct)
    {
        if(!context.TryGet(out var actor)||actor is null||!await store.IsAdminAsync(actor,ct))
            throw new UnauthorizedAccessException("Active institution administrator required.");
        return actor;
    }
    private static Guid Campus(Guid id) => id==Guid.Empty ? throw new ArgumentException("Campus required.") : id;
    private static (string Given,string Family) Names(string given,string family)
    {
        var g=(given??"").Trim();var f=(family??"").Trim();
        if(g.Length is < 2 or > 100||f.Length>100)
            throw new ArgumentException("Invalid student name.");
        return (g,f);
    }
    private static void Date(DateOnly date)
    {
        if(date==default||date> DateOnly.FromDateTime(DateTime.UtcNow.Date)||
           date<DateOnly.FromDateTime(DateTime.UtcNow.Date).AddYears(-110))
            throw new ArgumentException("Invalid date of birth.");
    }
    private static void Optional(string? value,int maximum)
    {
        if(value?.Length>maximum)throw new ArgumentException("Contact field too long.");
    }
    public async Task<AdmissionReceipt> SubmitPublicAsync(AdmissionSubmit input,CancellationToken ct)
    {
        Campus(input.CampusId);
        var (g,f)=Names(input.GivenName,input.FamilyName);
        Date(input.DateOfBirth);Optional(input.Email,254);Optional(input.Phone,24);
        var application=await store.SubmitPublicAsync(input with {GivenName=g,FamilyName=f},ct)
            ?? throw new StudentScopeException();
        return new AdmissionReceipt(application.Id,application.ApplicationNumber,"SUBMITTED");
    }

    public async Task<AdmissionRecord> SubmitAsync(AdmissionSubmit input,CancellationToken ct)
    {
        var actor=await Admin(ct);
        Campus(input.CampusId);
        var (g,f)=Names(input.GivenName,input.FamilyName);
        Date(input.DateOfBirth);Optional(input.Email,254);Optional(input.Phone,24);
        return await store.SubmitAsync(actor.TenantId,input with {GivenName=g,FamilyName=f},ct)
            ?? throw new StudentScopeException();
    }
    public async Task<IReadOnlyList<AdmissionRecord>> ApplicationsAsync(Guid campus,CancellationToken ct)
    {
        var actor=await Admin(ct);
        return await store.ApplicationsAsync(actor.TenantId,Campus(campus),ct);
    }
    public async Task DecideAsync(Guid campus,Guid id,string decision,CancellationToken ct)
    {
        var actor=await Admin(ct);
        if(decision is not ("APPROVED" or "REJECTED"))throw new ArgumentException("Invalid decision.");
        if(!await store.DecideAsync(actor.TenantId,Campus(campus),id,decision,Guid.Parse(actor.SubjectId),ct))
            throw new StudentScopeException();
    }
    public async Task<StudentRecord> CreateStudentAsync(StudentCreate input,CancellationToken ct)
    {
        var actor=await Admin(ct);Campus(input.CampusId);
        var (g,f)=Names(input.GivenName,input.FamilyName);Date(input.DateOfBirth);
        return await store.CreateStudentAsync(actor.TenantId,input with {GivenName=g,FamilyName=f},ct)
            ?? throw new StudentScopeException();
    }
    public async Task<IReadOnlyList<StudentRecord>> StudentsAsync(Guid campus,CancellationToken ct)
    {
        var actor=await Admin(ct);
        return await store.StudentsAsync(actor.TenantId,Campus(campus),ct);
    }
    public async Task<StudentRecord> StudentAsync(Guid campus,Guid student,CancellationToken ct)
    {
        var actor=await Admin(ct);
        return await store.StudentAsync(actor.TenantId,Campus(campus),student,ct)
            ?? throw new StudentScopeException();
    }
    public async Task<GuardianRecord> AddGuardianAsync(Guid campus,Guid student,GuardianCreate input,CancellationToken ct)
    {
        var actor=await Admin(ct);Campus(campus);
        var name=(input.FullName??"").Trim();
        if(name.Length is < 2 or > 160||input.Relationship is not ("MOTHER" or "FATHER" or "GUARDIAN" or "OTHER"))
            throw new ArgumentException("Invalid guardian.");
        Optional(input.Phone,24);Optional(input.Email,254);
        return await store.AddGuardianAsync(actor.TenantId,campus,student,input with {FullName=name},ct)
            ?? throw new StudentScopeException();
    }
    public async Task<IReadOnlyList<GuardianRecord>> GuardiansAsync(Guid campus,Guid student,CancellationToken ct)
    {
        var actor=await Admin(ct);
        return await store.GuardiansAsync(actor.TenantId,Campus(campus),student,ct);
    }
    public async Task<EnrollmentRecord> EnrollAsync(Guid campus,Guid student,EnrollmentCreate input,CancellationToken ct)
    {
        var actor=await Admin(ct);Campus(campus);
        if(input.AcademicItemId==Guid.Empty||input.AcademicYearId==Guid.Empty)throw new ArgumentException("Academic placement required.");
        return await store.EnrollAsync(actor.TenantId,campus,student,input,ct)
            ?? throw new StudentScopeException();
    }
    public async Task<IReadOnlyList<EnrollmentRecord>> EnrollmentsAsync(Guid campus,Guid student,CancellationToken ct)
    {
        var actor=await Admin(ct);
        return await store.EnrollmentsAsync(actor.TenantId,Campus(campus),student,ct);
    }
    public async Task LifecycleAsync(Guid campus,Guid student,LifecycleCommand input,CancellationToken ct)
    {
        var actor=await Admin(ct);Campus(campus);
        if(input.Action is not ("PROMOTE" or "TRANSFER" or "WITHDRAW")||input.Reason?.Length>400)
            throw new ArgumentException("Invalid lifecycle action.");
        if(input.Action=="PROMOTE"&&(input.AcademicYearId is null||input.AcademicItemId is null))
            throw new ArgumentException("Promotion requires destination year and academic item.");
        if(input.Action=="TRANSFER"&&(input.TargetCampusId is null||input.TargetCampusId==campus))
            throw new ArgumentException("Transfer requires another campus.");
        if(!await store.LifecycleAsync(actor.TenantId,campus,student,input,Guid.Parse(actor.SubjectId),ct))
            throw new StudentScopeException();
    }
    public async Task<DocumentRecord> UploadAsync(Guid campus,Guid student,string category,string filename,string mime,byte[] bytes,CancellationToken ct)
    {
        var actor=await Admin(ct);Campus(campus);
        if(category is not ("IDENTITY" or "BIRTH_CERTIFICATE" or "TRANSFER_CERTIFICATE" or "OTHER")||
           bytes.Length is < 4 or > 1048576||filename.Length is < 1 or > 180 ||
           filename.IndexOfAny(['/', '\\'])>=0||filename.Any(char.IsControl))
            throw new ArgumentException("Invalid document metadata or size.");
        // Content is checked using file signatures, not a user-provided extension alone.
        var pdf=mime=="application/pdf"&&bytes.AsSpan().StartsWith("%PDF-"u8);
        var png=mime=="image/png"&&bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10});
        var jpg=mime=="image/jpeg"&&bytes[0]==255&&bytes[1]==216&&bytes[2]==255;
        if(!pdf&&!png&&!jpg)throw new ArgumentException("Only valid PDF/PNG/JPEG files are accepted.");
        return await store.SaveDocumentAsync(actor.TenantId,campus,student,category,filename,mime,bytes,ct)
            ?? throw new StudentScopeException();
    }
    public async Task<IReadOnlyList<DocumentRecord>> DocumentsAsync(Guid campus,Guid student,CancellationToken ct)
    {
        var actor=await Admin(ct);
        return await store.DocumentsAsync(actor.TenantId,Campus(campus),student,ct);
    }
    public async Task<(string Name,string Mime,byte[] Bytes)> ReadDocumentAsync(Guid campus,Guid student,Guid doc,CancellationToken ct)
    {
        var actor=await Admin(ct);
        return await store.ReadDocumentAsync(actor.TenantId,Campus(campus),student,doc,ct)
            ?? throw new StudentScopeException();
    }
    public async Task ReviewDocumentAsync(Guid campus,Guid student,Guid doc,string decision,CancellationToken ct)
    {
        var actor=await Admin(ct);
        if(decision is not ("VERIFIED" or "REJECTED"))throw new ArgumentException("Invalid document decision.");
        if(!await store.ReviewDocumentAsync(actor.TenantId,Campus(campus),student,doc,decision,Guid.Parse(actor.SubjectId),ct))
            throw new StudentScopeException();
    }
}
public sealed class StudentScopeException:Exception { }
