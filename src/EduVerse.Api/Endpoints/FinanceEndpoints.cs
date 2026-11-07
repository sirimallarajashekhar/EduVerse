using EduVerse.Application.Finance;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Api.Endpoints;

/// <summary>Authenticated tenant-scoped finance API. Records manual/offline payments only.</summary>
public static class FinanceEndpoints
{
 public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder app)
 {
  var g=app.MapGroup("/api/v1/finance").RequireAuthorization();
  g.MapPost("/fees",async (FeeStructureCreate r,FinanceService s,CancellationToken ct)=>
   await Write(()=>s.AddFee(r,ct)));
  g.MapGet("/fees",async (Guid campusId,FinanceService s,CancellationToken ct)=>
   await Read(()=>s.Fees(campusId,ct)));
  g.MapPost("/charges",async (ChargeCreate r,FinanceService s,CancellationToken ct)=>
   await Write(()=>s.Assign(r,ct)));
  g.MapGet("/charges",async (Guid campusId,FinanceService s,CancellationToken ct)=>
   await Read(()=>s.Charges(campusId,ct)));
  g.MapGet("/charges/{id:guid}/installments",async (Guid id,Guid campusId,FinanceService s,CancellationToken ct)=>
   await Read(()=>s.Installments(campusId,id,ct)));
  g.MapPost("/concessions",async (ConcessionCreate r,FinanceService s,CancellationToken ct)=>
   await Write(()=>s.Concede(r,ct)));
  g.MapPost("/payments",async (PaymentCreate r,HttpContext http,FinanceService s,CancellationToken ct)=>
   await Write(()=>s.Collect(r,http.Request.Headers["Idempotency-Key"].ToString(),ct)));
  g.MapGet("/payments",async (Guid campusId,FinanceService s,CancellationToken ct)=>
   await Read(()=>s.Payments(campusId,ct)));
  g.MapGet("/payments/{id:guid}/receipt",async (Guid id,Guid campusId,FinanceService s,CancellationToken ct)=>
   await Read(()=>s.Receipt(campusId,id,ct)));
  g.MapPost("/refunds",async (RefundCreate r,HttpContext http,FinanceService s,CancellationToken ct)=>
   await Write(()=>s.Refund(r,http.Request.Headers["Idempotency-Key"].ToString(),ct)));
  g.MapPost("/reconciliation",async (ReconcileRequest r,FinanceService s,CancellationToken ct)=>
   await Change(()=>s.Reconcile(r,ct)));
  g.MapGet("/dashboard",async (Guid campusId,FinanceService s,CancellationToken ct)=>
   await Read(()=>s.Dashboard(campusId,ct)));
  return app;
 }
 private static async Task<IResult> Read<T>(Func<Task<T>> work)
 {
  try{return Results.Ok(await work());}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(FinanceScopeException){return Results.NotFound();}
  catch(ArgumentException ex){return Invalid(ex.Message);}
 }
 private static async Task<IResult> Write<T>(Func<Task<T>> work)
 {
  try{return Results.Ok(await work());}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(FinanceScopeException){return Results.NotFound();}
  catch(FinanceConflictException ex){return Results.Conflict(new {message=ex.Message});}
  catch(ArgumentException ex){return Invalid(ex.Message);}
  catch(MySqlException ex) when(ex.Number==1062)
   {return Results.Conflict(new {message="A fee assignment or identifier already exists."});}
 }
 private static async Task<IResult> Change(Func<Task> work)
 {
  try{await work();return Results.NoContent();}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(FinanceScopeException){return Results.NotFound();}
  catch(FinanceConflictException ex){return Results.Conflict(new {message=ex.Message});}
  catch(ArgumentException ex){return Invalid(ex.Message);}
 }
 private static IResult Invalid(string message)=>Results.ValidationProblem(
  new Dictionary<string,string[]> {["finance"]=[message]});
}
