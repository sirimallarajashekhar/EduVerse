using EduVerse.Application.OperationsModules;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Api.Endpoints;
public static class OperationsEndpoints
{
 public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
 {
  var g=app.MapGroup("/api/v1/operations").RequireAuthorization();
  g.MapPost("/titles",async (LibraryTitleCreate input,OperationsService s,CancellationToken ct)=>await Write(()=>s.Title(input,ct)));
  g.MapGet("/titles",async (Guid campusId,OperationsService s,CancellationToken ct)=>await Read(()=>s.Titles(campusId,ct)));
  g.MapPost("/copies",async (LibraryCopyCreate input,OperationsService s,CancellationToken ct)=>await Write(()=>s.Copy(input,ct)));
  g.MapGet("/copies",async (Guid campusId,OperationsService s,CancellationToken ct)=>await Read(()=>s.Copies(campusId,ct)));
  g.MapPost("/loans",async (LibraryLoanCreate input,OperationsService s,CancellationToken ct)=>await Write(()=>s.Borrow(input,ct)));
  g.MapGet("/loans",async (Guid campusId,OperationsService s,CancellationToken ct)=>await Read(()=>s.Loans(campusId,ct)));
  g.MapPost("/reservations",async (LibraryReservationCreate input,OperationsService s,CancellationToken ct)=>await Write(()=>s.Reserve(input,ct)));
  g.MapGet("/reservations",async (Guid campusId,OperationsService s,CancellationToken ct)=>await Read(()=>s.Reservations(campusId,ct)));
  g.MapPost("/vehicles",async (TransportVehicleCreate input,OperationsService s,CancellationToken ct)=>await Write(()=>s.Vehicle(input,ct)));
  g.MapGet("/vehicles",async (Guid campusId,OperationsService s,CancellationToken ct)=>await Read(()=>s.Vehicles(campusId,ct)));
  g.MapPost("/routes",async (TransportRouteCreate input,OperationsService s,CancellationToken ct)=>await Write(()=>s.Route(input,ct)));
  g.MapGet("/routes",async (Guid campusId,OperationsService s,CancellationToken ct)=>await Read(()=>s.Routes(campusId,ct)));
  g.MapPost("/stops",async (TransportStopCreate input,OperationsService s,CancellationToken ct)=>await Write(()=>s.Stop(input,ct)));
  g.MapGet("/stops",async (Guid campusId,OperationsService s,CancellationToken ct)=>await Read(()=>s.Stops(campusId,ct)));
  g.MapPost("/route-assignments",async (TransportAssignmentCreate input,OperationsService s,CancellationToken ct)=>await Write(()=>s.AssignRoute(input,ct)));
  g.MapGet("/route-assignments",async (Guid campusId,OperationsService s,CancellationToken ct)=>await Read(()=>s.RouteAssignments(campusId,ct)));
  g.MapPost("/hostel-blocks",async (HostelBlockCreate input,OperationsService s,CancellationToken ct)=>await Write(()=>s.Block(input,ct)));
  g.MapGet("/hostel-blocks",async (Guid campusId,OperationsService s,CancellationToken ct)=>await Read(()=>s.Blocks(campusId,ct)));
  g.MapPost("/hostel-rooms",async (HostelRoomCreate input,OperationsService s,CancellationToken ct)=>await Write(()=>s.Room(input,ct)));
  g.MapGet("/hostel-rooms",async (Guid campusId,OperationsService s,CancellationToken ct)=>await Read(()=>s.Rooms(campusId,ct)));
  g.MapPost("/hostel-beds",async (HostelBedCreate input,OperationsService s,CancellationToken ct)=>await Write(()=>s.Bed(input,ct)));
  g.MapGet("/hostel-beds",async (Guid campusId,OperationsService s,CancellationToken ct)=>await Read(()=>s.Beds(campusId,ct)));
  g.MapPost("/hostel-allocations",async (HostelAllocationCreate input,OperationsService s,CancellationToken ct)=>await Write(()=>s.AllocateBed(input,ct)));
  g.MapGet("/hostel-allocations",async (Guid campusId,OperationsService s,CancellationToken ct)=>await Read(()=>s.Allocations(campusId,ct)));
  g.MapPost("/loans/{id:guid}/return",async (Guid id,Guid campusId,OperationsService s,CancellationToken ct)=>
   await Write(()=>s.Return(campusId,id,ct)));
  g.MapPost("/route-assignments/{id:guid}/release",async (Guid id,Guid campusId,OperationsService s,CancellationToken ct)=>
   await Change(()=>s.ReleaseRoute(campusId,id,ct)));
  g.MapPost("/hostel-allocations/{id:guid}/vacate",async (Guid id,Guid campusId,OperationsService s,CancellationToken ct)=>
   await Change(()=>s.Vacate(campusId,id,ct)));
  g.MapGet("/dashboard",async (Guid campusId,OperationsService s,CancellationToken ct)=>
   await Read(()=>s.Dashboard(campusId,ct)));
  return app;
 }
 private static async Task<IResult> Read<T>(Func<Task<T>> f)
 {
  try{return Results.Ok(await f());}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(OperationsScopeException){return Results.NotFound();}
  catch(ArgumentException e){return Invalid(e.Message);}
 }
 private static async Task<IResult> Write<T>(Func<Task<T>> f)
 {
  try{return Results.Ok(await f());}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(OperationsScopeException){return Results.NotFound();}
  catch(OperationsConflictException e){return Results.Conflict(new {message=e.Message});}
  catch(ArgumentException e){return Invalid(e.Message);}
  catch(MySqlException e) when(e.Number==1062){return Results.Conflict(new {message="A unique assignment or code already exists."});}
 }
 private static async Task<IResult> Change(Func<Task> f)
 {
  try{await f();return Results.NoContent();}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(OperationsScopeException){return Results.NotFound();}
  catch(OperationsConflictException e){return Results.Conflict(new {message=e.Message});}
  catch(ArgumentException e){return Invalid(e.Message);}
 }
 private static IResult Invalid(string message)=>Results.ValidationProblem(
  new Dictionary<string,string[]> {["operations"]=[message]});
}
