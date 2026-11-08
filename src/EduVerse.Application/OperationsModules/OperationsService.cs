using EduVerse.Application.Tenancy;
using EduVerse.Contracts;
using EduVerse.Domain;
namespace EduVerse.Application.OperationsModules;

/// <summary>API-owned authorization, input validation and scoped operations.</summary>
public sealed class OperationsService(IOperationsStore store,ITenantContext context, EduVerse.Application.Identity.TenantReadAuthorizer reader)
{
 private async Task<Guid> Tenant(CancellationToken ct)
 {
  if(!context.TryGet(out var actor)||actor is null||!await store.Admin(actor,ct))
   throw new UnauthorizedAccessException("Active tenant administrator required.");
  return actor.TenantId;
 }
 private static Guid Id(Guid g)=>g==Guid.Empty?throw new ArgumentException("Identifier is required."):g;
 private static string Text(string? s,int max)
 {
  s=(s??"").Trim();if(s.Length is < 1 or > 180||s.Length>max)throw new ArgumentException("Invalid required text.");
  return s;
 }
 private static string Code(string? s,int max)
 {
  var t=Text(s,max).ToUpperInvariant();
  if(!t.All(c=>c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-'))
   throw new ArgumentException("Invalid code characters.");
  return t;
 }
 private static T Found<T>(T? record) where T:class=>record??throw new OperationsScopeException();
 public async Task<LibraryTitle> Title(LibraryTitleCreate r,CancellationToken ct)=>
  Found(await store.Title(await Tenant(ct),r with {CampusId=Id(r.CampusId),Title=Text(r.Title,180),Author=Text(r.Author,160)},ct));
 public async Task<IReadOnlyList<LibraryTitle>> Titles(Guid c,CancellationToken ct)=>await store.Titles((await reader.RequireAsync("operations.read",c,ct)).TenantId,Id(c),ct);
 public async Task<LibraryCopy> Copy(LibraryCopyCreate r,CancellationToken ct)=>
  Found(await store.Copy(await Tenant(ct),r with {CampusId=Id(r.CampusId),TitleId=Id(r.TitleId),Barcode=Code(r.Barcode,64)},ct));
 public async Task<IReadOnlyList<LibraryCopy>> Copies(Guid c,CancellationToken ct)=>await store.Copies((await reader.RequireAsync("operations.read",c,ct)).TenantId,Id(c),ct);
 public async Task<LibraryLoan> Borrow(LibraryLoanCreate r,CancellationToken ct)
 {
  if(r.DueOn<DateOnly.FromDateTime(DateTime.UtcNow.Date)||r.DueOn.DayNumber-DateOnly.FromDateTime(DateTime.UtcNow.Date).DayNumber>90)
   throw new ArgumentException("Due date must be within 90 days.");
  return Found(await store.Borrow(await Tenant(ct),r with {CampusId=Id(r.CampusId),CopyId=Id(r.CopyId),StudentId=Id(r.StudentId)},ct));
 }
 public async Task<LibraryLoan> Return(Guid c,Guid loan,CancellationToken ct)=>Found(await store.Return(await Tenant(ct),Id(c),Id(loan),ct));
 public async Task<IReadOnlyList<LibraryLoan>> Loans(Guid c,CancellationToken ct)=>await store.Loans(await Tenant(ct),Id(c),ct);
 public async Task<LibraryReservation> Reserve(LibraryReservationCreate r,CancellationToken ct)=>
  Found(await store.Reserve(await Tenant(ct),r with {CampusId=Id(r.CampusId),TitleId=Id(r.TitleId),StudentId=Id(r.StudentId)},ct));
 public async Task<IReadOnlyList<LibraryReservation>> Reservations(Guid c,CancellationToken ct)=>await store.Reservations(await Tenant(ct),Id(c),ct);
 public async Task<TransportVehicle> Vehicle(TransportVehicleCreate r,CancellationToken ct)
 {
  if(r.Capacity is < 1 or > 100||r.DriverPhone?.Length>30)throw new ArgumentException("Invalid vehicle details.");
  return Found(await store.Vehicle(await Tenant(ct),r with {CampusId=Id(r.CampusId),Registration=Code(r.Registration.Replace(" ","").Replace("-",""),32),DriverName=Text(r.DriverName,120)},ct));
 }
 public async Task<IReadOnlyList<TransportVehicle>> Vehicles(Guid c,CancellationToken ct)=>await store.Vehicles((await reader.RequireAsync("operations.read",c,ct)).TenantId,Id(c),ct);
 public async Task<TransportRoute> Route(TransportRouteCreate r,CancellationToken ct)=>
  Found(await store.Route(await Tenant(ct),r with {CampusId=Id(r.CampusId),VehicleId=Id(r.VehicleId),Code=Code(r.Code,30),Name=Text(r.Name,120)},ct));
 public async Task<IReadOnlyList<TransportRoute>> Routes(Guid c,CancellationToken ct)=>await store.Routes((await reader.RequireAsync("operations.read",c,ct)).TenantId,Id(c),ct);
 public async Task<TransportStop> Stop(TransportStopCreate r,CancellationToken ct)
 {
  if(r.Order is < 1 or > 500)throw new ArgumentException("Invalid stop sequence.");
  return Found(await store.Stop(await Tenant(ct),r with {CampusId=Id(r.CampusId),RouteId=Id(r.RouteId),Name=Text(r.Name,120)},ct));
 }
 public async Task<IReadOnlyList<TransportStop>> Stops(Guid c,CancellationToken ct)=>await store.Stops((await reader.RequireAsync("operations.read",c,ct)).TenantId,Id(c),ct);
 public async Task<TransportAssignment> AssignRoute(TransportAssignmentCreate r,CancellationToken ct)=>
  Found(await store.AssignRoute(await Tenant(ct),r with {CampusId=Id(r.CampusId),StudentId=Id(r.StudentId),RouteId=Id(r.RouteId),StopId=Id(r.StopId)},ct));
 public async Task ReleaseRoute(Guid c,Guid a,CancellationToken ct)
 {if(!await store.ReleaseRoute(await Tenant(ct),Id(c),Id(a),ct))throw new OperationsScopeException();}
 public async Task<IReadOnlyList<TransportAssignment>> RouteAssignments(Guid c,CancellationToken ct)=>await store.RouteAssignments(await Tenant(ct),Id(c),ct);
 public async Task<HostelBlock> Block(HostelBlockCreate r,CancellationToken ct)=>
  Found(await store.Block(await Tenant(ct),r with {CampusId=Id(r.CampusId),Code=Code(r.Code,30),Name=Text(r.Name,120)},ct));
 public async Task<IReadOnlyList<HostelBlock>> Blocks(Guid c,CancellationToken ct)=>await store.Blocks(await Tenant(ct),Id(c),ct);
 public async Task<HostelRoom> Room(HostelRoomCreate r,CancellationToken ct)=>
  Found(await store.Room(await Tenant(ct),r with {CampusId=Id(r.CampusId),BlockId=Id(r.BlockId),Number=Text(r.Number,30)},ct));
 public async Task<IReadOnlyList<HostelRoom>> Rooms(Guid c,CancellationToken ct)=>await store.Rooms(await Tenant(ct),Id(c),ct);
 public async Task<HostelBed> Bed(HostelBedCreate r,CancellationToken ct)=>
  Found(await store.Bed(await Tenant(ct),r with {CampusId=Id(r.CampusId),RoomId=Id(r.RoomId),Number=Text(r.Number,30)},ct));
 public async Task<IReadOnlyList<HostelBed>> Beds(Guid c,CancellationToken ct)=>await store.Beds(await Tenant(ct),Id(c),ct);
 public async Task<HostelAllocation> AllocateBed(HostelAllocationCreate r,CancellationToken ct)=>
  Found(await store.AllocateBed(await Tenant(ct),r with {CampusId=Id(r.CampusId),StudentId=Id(r.StudentId),BedId=Id(r.BedId)},ct));
 public async Task Vacate(Guid c,Guid a,CancellationToken ct)
 {if(!await store.Vacate(await Tenant(ct),Id(c),Id(a),ct))throw new OperationsScopeException();}
 public async Task<IReadOnlyList<HostelAllocation>> Allocations(Guid c,CancellationToken ct)=>await store.Allocations(await Tenant(ct),Id(c),ct);
 public async Task<OperationsDashboard> Dashboard(Guid c,CancellationToken ct)=>await store.Dashboard((await reader.RequireAsync("operations.read",c,ct)).TenantId,Id(c),ct);
}
