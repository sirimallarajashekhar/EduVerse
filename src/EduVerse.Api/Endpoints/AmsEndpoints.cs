using EduVerse.Application.Ams;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Api.Endpoints;
public static class AmsEndpoints
{
 public static IEndpointRouteBuilder MapAmsEndpoints(this IEndpointRouteBuilder app)
 {
  var g=app.MapGroup("/api/v1/ams").RequireAuthorization();
  g.MapPost("/categories",async (AmsCategoryCreate r,AmsService s,CancellationToken ct)=>
   await Write(()=>s.AddCategory(r,ct)));
  g.MapGet("/categories",async (Guid campusId,AmsService s,CancellationToken ct)=>
   await Read(()=>s.Categories(campusId,ct)));
  g.MapPost("/vendors",async (AmsVendorCreate r,AmsService s,CancellationToken ct)=>
   await Write(()=>s.AddVendor(r,ct)));
  g.MapGet("/vendors",async (Guid campusId,AmsService s,CancellationToken ct)=>
   await Read(()=>s.Vendors(campusId,ct)));
  g.MapPost("/assets",async (AmsAssetCreate r,AmsService s,CancellationToken ct)=>
   await Write(()=>s.Register(r,ct)));
  g.MapGet("/assets",async (Guid campusId,AmsService s,CancellationToken ct)=>
   await Read(()=>s.Assets(campusId,ct)));
  g.MapPost("/assets/allocate",async (AmsAllocation r,AmsService s,CancellationToken ct)=>
   await Write(()=>s.Allocate(r,ct)));
  g.MapPost("/assets/lifecycle",async (AmsAssetAction r,AmsService s,CancellationToken ct)=>
   await Write(()=>s.Lifecycle(r,ct)));
  g.MapGet("/assets/{asset:guid}/events",async (Guid asset,Guid campusId,AmsService s,CancellationToken ct)=>
   await Read(()=>s.Events(campusId,asset,ct)));
  g.MapPost("/maintenance",async (AmsMaintenanceCreate r,AmsService s,CancellationToken ct)=>
   await Write(()=>s.CreateWorkOrder(r,ct)));
  g.MapGet("/maintenance",async (Guid campusId,AmsService s,CancellationToken ct)=>
   await Read(()=>s.WorkOrders(campusId,ct)));
  g.MapPost("/maintenance/{id:guid}/complete",async (Guid id,Guid campusId,AmsService s,CancellationToken ct)=>
   await Change(()=>s.CompleteWorkOrder(campusId,id,ct)));
  g.MapPost("/stock",async (AmsStockCreate r,AmsService s,CancellationToken ct)=>
   await Write(()=>s.AddStock(r,ct)));
  g.MapGet("/stock",async (Guid campusId,AmsService s,CancellationToken ct)=>
   await Read(()=>s.Stock(campusId,ct)));
  g.MapPost("/stock/movements",async (AmsStockMove r,HttpContext http,AmsService s,CancellationToken ct)=>
   await Write(()=>s.MoveStock(r,http.Request.Headers["Idempotency-Key"].ToString(),ct)));
  g.MapPost("/purchases",async (AmsPurchaseCreate r,AmsService s,CancellationToken ct)=>
   await Write(()=>s.AddPurchase(r,ct)));
  g.MapGet("/purchases",async (Guid campusId,AmsService s,CancellationToken ct)=>
   await Read(()=>s.Purchases(campusId,ct)));
  g.MapGet("/dashboard",async (Guid campusId,AmsService s,CancellationToken ct)=>
   await Read(()=>s.Dashboard(campusId,ct)));
  return app;
 }
 private static async Task<IResult> Read<T>(Func<Task<T>> work)
 {
  try{return Results.Ok(await work());}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(AmsScopeException){return Results.NotFound();}
  catch(ArgumentException e){return Invalid(e.Message);}
 }
 private static async Task<IResult> Write<T>(Func<Task<T>> work)
 {
  try{return Results.Ok(await work());}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(AmsScopeException){return Results.NotFound();}
  catch(AmsConflictException e){return Results.Conflict(new {message=e.Message});}
  catch(ArgumentException e){return Invalid(e.Message);}
  catch(MySqlException e) when(e.Number==1062){return Results.Conflict(new {message="Duplicate code or allocation."});}
 }
 private static async Task<IResult> Change(Func<Task> work)
 {
  try{await work();return Results.NoContent();}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(AmsScopeException){return Results.NotFound();}
  catch(AmsConflictException e){return Results.Conflict(new {message=e.Message});}
  catch(ArgumentException e){return Invalid(e.Message);}
 }
 private static IResult Invalid(string message)=>Results.ValidationProblem(
  new Dictionary<string,string[]> {["ams"]=[message]});
}
