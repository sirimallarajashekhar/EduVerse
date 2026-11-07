using EduVerse.Contracts;
using EduVerse.Domain;

namespace EduVerse.Application.OperationsModules;

public interface IOperationsStore
{
 Task<bool> Admin(TenantIdentity actor,CancellationToken ct);
 Task<LibraryTitle?> Title(Guid tenant,LibraryTitleCreate r,CancellationToken ct);
 Task<IReadOnlyList<LibraryTitle>> Titles(Guid tenant,Guid campus,CancellationToken ct);
 Task<LibraryCopy?> Copy(Guid tenant,LibraryCopyCreate r,CancellationToken ct);
 Task<IReadOnlyList<LibraryCopy>> Copies(Guid tenant,Guid campus,CancellationToken ct);
 Task<LibraryLoan?> Borrow(Guid tenant,LibraryLoanCreate r,CancellationToken ct);
 Task<LibraryLoan?> Return(Guid tenant,Guid campus,Guid loan,CancellationToken ct);
 Task<IReadOnlyList<LibraryLoan>> Loans(Guid tenant,Guid campus,CancellationToken ct);
 Task<LibraryReservation?> Reserve(Guid tenant,LibraryReservationCreate r,CancellationToken ct);
 Task<IReadOnlyList<LibraryReservation>> Reservations(Guid tenant,Guid campus,CancellationToken ct);
 Task<TransportVehicle?> Vehicle(Guid tenant,TransportVehicleCreate r,CancellationToken ct);
 Task<IReadOnlyList<TransportVehicle>> Vehicles(Guid tenant,Guid campus,CancellationToken ct);
 Task<TransportRoute?> Route(Guid tenant,TransportRouteCreate r,CancellationToken ct);
 Task<IReadOnlyList<TransportRoute>> Routes(Guid tenant,Guid campus,CancellationToken ct);
 Task<TransportStop?> Stop(Guid tenant,TransportStopCreate r,CancellationToken ct);
 Task<IReadOnlyList<TransportStop>> Stops(Guid tenant,Guid campus,CancellationToken ct);
 Task<TransportAssignment?> AssignRoute(Guid tenant,TransportAssignmentCreate r,CancellationToken ct);
 Task<bool> ReleaseRoute(Guid tenant,Guid campus,Guid assignment,CancellationToken ct);
 Task<IReadOnlyList<TransportAssignment>> RouteAssignments(Guid tenant,Guid campus,CancellationToken ct);
 Task<HostelBlock?> Block(Guid tenant,HostelBlockCreate r,CancellationToken ct);
 Task<IReadOnlyList<HostelBlock>> Blocks(Guid tenant,Guid campus,CancellationToken ct);
 Task<HostelRoom?> Room(Guid tenant,HostelRoomCreate r,CancellationToken ct);
 Task<IReadOnlyList<HostelRoom>> Rooms(Guid tenant,Guid campus,CancellationToken ct);
 Task<HostelBed?> Bed(Guid tenant,HostelBedCreate r,CancellationToken ct);
 Task<IReadOnlyList<HostelBed>> Beds(Guid tenant,Guid campus,CancellationToken ct);
 Task<HostelAllocation?> AllocateBed(Guid tenant,HostelAllocationCreate r,CancellationToken ct);
 Task<bool> Vacate(Guid tenant,Guid campus,Guid allocation,CancellationToken ct);
 Task<IReadOnlyList<HostelAllocation>> Allocations(Guid tenant,Guid campus,CancellationToken ct);
 Task<OperationsDashboard> Dashboard(Guid tenant,Guid campus,CancellationToken ct);
}
public sealed class OperationsScopeException:Exception {}
public sealed class OperationsConflictException(string message):Exception(message) {}
