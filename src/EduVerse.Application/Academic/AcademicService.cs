using EduVerse.Application.Tenancy;
using EduVerse.Contracts;
using EduVerse.Domain;

namespace EduVerse.Application.Academic;

/// <summary>All academic writes are authorized against live tenant membership and plan state.</summary>
public sealed class AcademicService(IAcademicStore store, ITenantContext context)
{
    private static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal)
    { "TERM","SEMESTER","GRADE","CLASS","SECTION","PROGRAM","COURSE","BATCH","SUBJECT" };
    private static readonly IReadOnlySet<string> EventKinds = new HashSet<string>(StringComparer.Ordinal)
    { "HOLIDAY","EVENT","EXAM","ACADEMIC" };

    private async Task<TenantIdentity> RequireAdminAsync(CancellationToken ct)
    {
        if (!context.TryGet(out var identity) || identity is null ||
            !await store.IsActiveAdminWithAcademicFeatureAsync(identity,ct))
            throw new UnauthorizedAccessException("Active tenant administrator and academic entitlement required.");
        return identity;
    }

    public async Task<IReadOnlyList<AcademicYear>> YearsAsync(Guid campusId,CancellationToken ct)
    {
        var actor=await RequireAdminAsync(ct);
        return await store.YearsAsync(actor.TenantId,RequireCampus(campusId),ct);
    }

    public async Task<AcademicYear> CreateYearAsync(AcademicYearCreate request,CancellationToken ct)
    {
        var actor=await RequireAdminAsync(ct);
        var (code,name)=Normalize(request.Code,request.Name);
        RequireCampus(request.CampusId);
        ValidateDates(request.StartsOn,request.EndsOn);
        if(request.EndsOn.DayNumber-request.StartsOn.DayNumber>1096)
            throw new ArgumentException("Academic year may not exceed three years.");
        return await store.CreateYearAsync(actor.TenantId,request with {Code=code,Name=name},ct)
            ?? throw new AcademicScopeException();
    }

    public async Task<IReadOnlyList<AcademicItem>> ItemsAsync(Guid campusId,string? kind,CancellationToken ct)
    {
        var actor=await RequireAdminAsync(ct);
        if(kind is not null && !Kinds.Contains(kind))throw new ArgumentException("Unknown academic item kind.");
        return await store.ItemsAsync(actor.TenantId,RequireCampus(campusId),kind,ct);
    }

    public async Task<AcademicItem> CreateItemAsync(AcademicItemCreate request,CancellationToken ct)
    {
        var actor=await RequireAdminAsync(ct);
        RequireCampus(request.CampusId);
        var kind=request.Kind?.Trim().ToUpperInvariant() ?? "";
        if(!Kinds.Contains(kind))throw new ArgumentException("Unknown academic item kind.");
        var (code,name)=Normalize(request.Code,request.Name);
        if(request.StartsOn.HasValue != request.EndsOn.HasValue)
            throw new ArgumentException("Specify both start and end dates or neither.");
        if(request.StartsOn.HasValue)ValidateDates(request.StartsOn.Value,request.EndsOn!.Value);

        if(kind is "TERM" or "SEMESTER" or "SECTION" or "BATCH" && request.AcademicYearId is null)
            throw new ArgumentException("Term, semester, section and batch require an academic year.");
        if(kind is "COURSE" or "SECTION" or "BATCH" && request.ParentId is null)
            throw new ArgumentException("Course, section and batch require a parent academic record.");
        if(kind is "PROGRAM" or "GRADE" or "TERM" && request.ParentId is not null)
            throw new ArgumentException("This item kind cannot have a parent.");
        if(request.ParentId==Guid.Empty || request.DepartmentId==Guid.Empty ||
           request.AcademicYearId==Guid.Empty)
            throw new ArgumentException("Invalid relationship identifier.");

        return await store.CreateItemAsync(actor.TenantId,
            request with {Kind=kind,Code=code,Name=name},ct)
            ?? throw new AcademicScopeException();
    }

    public async Task<IReadOnlyList<FacultyAssignment>> FacultyAsync(Guid campusId,CancellationToken ct)
    {
        var actor=await RequireAdminAsync(ct);
        return await store.FacultyAsync(actor.TenantId,RequireCampus(campusId),ct);
    }

    public async Task<FacultyAssignment> AssignFacultyAsync(FacultyAssignmentCreate request,CancellationToken ct)
    {
        var actor=await RequireAdminAsync(ct);
        RequireCampus(request.CampusId);
        if(request.SubjectId==Guid.Empty || request.AcademicYearId==Guid.Empty || request.FacultyUserId==Guid.Empty)
            throw new ArgumentException("Academic year, subject and faculty are required.");
        return await store.AssignFacultyAsync(actor.TenantId,request,ct)
            ?? throw new AcademicScopeException();
    }

    public async Task<IReadOnlyList<AcademicEvent>> EventsAsync(Guid campusId,CancellationToken ct)
    {
        var actor=await RequireAdminAsync(ct);
        return await store.EventsAsync(actor.TenantId,RequireCampus(campusId),ct);
    }

    public async Task<AcademicEvent> CreateEventAsync(AcademicEventCreate request,CancellationToken ct)
    {
        var actor=await RequireAdminAsync(ct);
        RequireCampus(request.CampusId);
        if(request.AcademicYearId==Guid.Empty || !EventKinds.Contains(request.Kind ?? "") ||
           string.IsNullOrWhiteSpace(request.Title) || request.Title.Length>160 || request.Notes?.Length>500)
            throw new ArgumentException("Invalid calendar event.");
        ValidateDates(request.StartsOn,request.EndsOn);
        return await store.CreateEventAsync(actor.TenantId,request with {Title=request.Title.Trim()},ct)
            ?? throw new AcademicScopeException();
    }

    public async Task<AcademicOverview> OverviewAsync(Guid campusId,CancellationToken ct)
    {
        var actor=await RequireAdminAsync(ct);
        return await store.OverviewAsync(actor.TenantId,RequireCampus(campusId),ct);
    }

    private static Guid RequireCampus(Guid id) =>
        id==Guid.Empty ? throw new ArgumentException("Campus is required.") : id;
    private static (string Code,string Name) Normalize(string? code,string? name)
    {
        var normalized=(code??"").Trim().ToUpperInvariant();
        var display=(name??"").Trim();
        if(normalized.Length is < 2 or > 30 ||
           !normalized.All(c=>c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-') ||
           display.Length is < 2 or > 160)
            throw new ArgumentException("Invalid academic code or name.");
        return (normalized,display);
    }
    private static void ValidateDates(DateOnly from,DateOnly until)
    {
        if(from==default || until==default || from>until)
            throw new ArgumentException("Start and end dates are invalid.");
    }
}
