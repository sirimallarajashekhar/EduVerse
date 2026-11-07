namespace EduVerse.Contracts;

public sealed record LibraryTitleCreate(Guid CampusId,string Title,string Author,string? Isbn);
public sealed record LibraryTitle(Guid Id,Guid CampusId,string Title,string Author,string? Isbn);
public sealed record LibraryCopyCreate(Guid CampusId,Guid TitleId,string Barcode);
public sealed record LibraryCopy(Guid Id,Guid TitleId,string Barcode,string Status);
public sealed record LibraryLoanCreate(Guid CampusId,Guid CopyId,Guid StudentId,DateOnly DueOn);
public sealed record LibraryLoan(Guid Id,Guid CopyId,Guid StudentId,DateOnly BorrowedOn,DateOnly DueOn,DateOnly? ReturnedOn,decimal Fine,string Status);
public sealed record LibraryReservationCreate(Guid CampusId,Guid TitleId,Guid StudentId);
public sealed record LibraryReservation(Guid Id,Guid TitleId,Guid StudentId,string Status);
public sealed record TransportVehicleCreate(Guid CampusId,string Registration,int Capacity,string DriverName,string? DriverPhone);
public sealed record TransportVehicle(Guid Id,string Registration,int Capacity,string DriverName,string? DriverPhone);
public sealed record TransportRouteCreate(Guid CampusId,Guid VehicleId,string Code,string Name);
public sealed record TransportRoute(Guid Id,Guid VehicleId,string Code,string Name);
public sealed record TransportStopCreate(Guid CampusId,Guid RouteId,string Name,int Order);
public sealed record TransportStop(Guid Id,Guid RouteId,string Name,int Order);
public sealed record TransportAssignmentCreate(Guid CampusId,Guid StudentId,Guid RouteId,Guid StopId);
public sealed record TransportAssignment(Guid Id,Guid StudentId,Guid RouteId,Guid StopId,string Status);
public sealed record HostelBlockCreate(Guid CampusId,string Code,string Name);
public sealed record HostelBlock(Guid Id,string Code,string Name);
public sealed record HostelRoomCreate(Guid CampusId,Guid BlockId,string Number);
public sealed record HostelRoom(Guid Id,Guid BlockId,string Number);
public sealed record HostelBedCreate(Guid CampusId,Guid RoomId,string Number);
public sealed record HostelBed(Guid Id,Guid RoomId,string Number,bool Occupied);
public sealed record HostelAllocationCreate(Guid CampusId,Guid StudentId,Guid BedId);
public sealed record HostelAllocation(Guid Id,Guid StudentId,Guid BedId,string Status);
public sealed record OperationsDashboard(int Titles,int CopiesAvailable,int LoansOpen,int OverdueLoans,decimal OutstandingFines,
 int TransportRoutes,int TransportAssigned,int HostelBeds,int HostelOccupied);
