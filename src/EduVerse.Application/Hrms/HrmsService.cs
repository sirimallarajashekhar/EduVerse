using EduVerse.Application.Tenancy;
using EduVerse.Contracts;
using EduVerse.Domain;
namespace EduVerse.Application.Hrms;
public sealed class HrmsService(IHrmsStore store,ITenantContext context)
{
 private async Task<TenantIdentity> Admin(CancellationToken ct)
 {
  if(!context.TryGet(out var actor)||actor is null||
    !await store.IsAdminAsync(actor,ct))
   throw new UnauthorizedAccessException("Active HRMS admin required.");
  return actor;
 }
 private static Guid Id(Guid x)=>x==Guid.Empty?throw new ArgumentException("Identifier required."):x;
 private static string Text(string? x,int max)
 {
  var s=(x??"").Trim();
  if(s.Length is < 1 or > 400||s.Length>max)throw new ArgumentException("Invalid text field.");
  return s;
 }
 private static decimal Amount(decimal amount,decimal max,bool allowZero=false)
 {
  if(amount<0||!allowZero&&amount==0||amount>max||decimal.Round(amount,2)!=amount)
   throw new ArgumentException("Amount must be two-decimal positive value.");
  return amount;
 }
 public async Task<HrDesignation> Designate(HrDesignationCreate input,CancellationToken ct)
 {
  var a=await Admin(ct);Id(input.CampusId);
  return await store.Designate(a.TenantId,input with {Code=Text(input.Code,30).ToUpperInvariant(),
   Title=Text(input.Title,120)},ct)??throw new HrScopeException();
 }
 public async Task<IReadOnlyList<HrDesignation>> Designations(Guid campus,CancellationToken ct)
 { var a=await Admin(ct);return await store.Designations(a.TenantId,Id(campus),ct); }
 public async Task<HrEmployee> Hire(HrEmployeeCreate input,CancellationToken ct)
 {
  var a=await Admin(ct);Id(input.CampusId);
  if(input.Type is not ("FACULTY" or "STAFF" or "CONTRACT")||input.JoinedOn==default||
    input.JoinedOn>DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)))throw new ArgumentException("Invalid employment details.");
  if(input.WorkEmail?.Length>254)throw new ArgumentException("Email too long.");
  return await store.Hire(a.TenantId,a.SubjectId,input with {
   GivenName=Text(input.GivenName,100),FamilyName=Text(input.FamilyName,100)},ct)
    ??throw new HrScopeException();
 }
 public async Task<IReadOnlyList<HrEmployee>> Employees(Guid campus,CancellationToken ct)
 {var a=await Admin(ct);return await store.Employees(a.TenantId,Id(campus),ct);}
 public async Task Lifecycle(Guid campus,Guid employee,HrLifecycle input,CancellationToken ct)
 {
  var a=await Admin(ct);Id(campus);Id(employee);
  if(input.Status is not ("ACTIVE" or "ON_LEAVE" or "EXITED")||input.Note?.Length>400)
   throw new ArgumentException("Invalid employment transition.");
  if(!await store.Lifecycle(a.TenantId,a.SubjectId,campus,employee,input,ct))throw new HrScopeException();
 }
 public async Task<HrDocument> Upload(Guid campus,Guid employee,string type,string filename,string mime,byte[] bytes,CancellationToken ct)
 {
  var a=await Admin(ct);Id(campus);Id(employee);
  if(type is not ("CONTRACT" or "QUALIFICATION" or "IDENTITY" or "OTHER")||
    filename.Length is < 1 or > 180||filename.IndexOfAny(['/', '\\'])>=0||
    filename.Any(char.IsControl)||bytes.Length is < 4 or > 1048576)
   throw new ArgumentException("Invalid file.");
  if(!(mime=="application/pdf"&&bytes.AsSpan().StartsWith("%PDF-"u8)) &&
    !(mime=="image/png"&&bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10})) &&
    !(mime=="image/jpeg"&&bytes[0]==255&&bytes[1]==216&&bytes[2]==255))
   throw new ArgumentException("Only PDF, PNG and JPEG content is accepted.");
  return await store.Upload(a.TenantId,campus,employee,type,filename,mime,bytes,ct)??throw new HrScopeException();
 }
 public async Task<IReadOnlyList<HrDocument>> Documents(Guid campus,Guid employee,CancellationToken ct)
 {var a=await Admin(ct);return await store.Documents(a.TenantId,Id(campus),Id(employee),ct);}
 public async Task<(string FileName,byte[] Bytes)> Download(Guid campus,Guid employee,Guid doc,CancellationToken ct)
 {var a=await Admin(ct);return await store.Download(a.TenantId,Id(campus),Id(employee),Id(doc),ct)??throw new HrScopeException();}
 public async Task<HrLeaveType> LeaveType(HrLeaveTypeCreate input,CancellationToken ct)
 {
  var a=await Admin(ct);Id(input.CampusId);Amount(input.AnnualDays,366,true);
  return await store.LeaveType(a.TenantId,input with {
   Code=Text(input.Code,30).ToUpperInvariant(),Title=Text(input.Title,120)},ct)??throw new HrScopeException();
 }
 public async Task<IReadOnlyList<HrLeaveType>> LeaveTypes(Guid campus,CancellationToken ct)
 {var a=await Admin(ct);return await store.LeaveTypes(a.TenantId,Id(campus),ct);}
 public async Task<HrLeave> RequestLeave(HrLeaveCreate input,CancellationToken ct)
 {
  var a=await Admin(ct);Id(input.CampusId);Id(input.EmployeeId);Id(input.LeaveTypeId);
  if(input.From==default||input.To<input.From||input.From.Year!=input.To.Year||
    input.To.DayNumber-input.From.DayNumber+1>366)
   throw new ArgumentException("Leave must be within one calendar year.");
  return await store.RequestLeave(a.TenantId,input with {Reason=Text(input.Reason,300)},ct)??throw new HrScopeException();
 }
 public async Task<IReadOnlyList<HrLeave>> Leaves(Guid campus,CancellationToken ct)
 {var a=await Admin(ct);return await store.Leaves(a.TenantId,Id(campus),ct);}
 public async Task DecideLeave(Guid campus,Guid leave,string decision,CancellationToken ct)
 {
  var a=await Admin(ct);Id(campus);Id(leave);
  if(decision is not ("APPROVED" or "REJECTED"))throw new ArgumentException("Invalid decision.");
  if(!await store.DecideLeave(a.TenantId,a.SubjectId,campus,leave,decision,ct))throw new HrScopeException();
 }
 public async Task<HrLeaveBalance> Balance(Guid campus,Guid employee,Guid type,int year,CancellationToken ct)
 {
  var a=await Admin(ct);Id(campus);Id(employee);Id(type);
  if(year is < 2000 or > 2100)throw new ArgumentException("Invalid year.");
  return await store.Balance(a.TenantId,campus,employee,type,year,ct)??throw new HrScopeException();
 }
 public async Task<HrSalary> Salary(HrSalaryCreate input,CancellationToken ct)
 {
  var a=await Admin(ct);Id(input.CampusId);Id(input.EmployeeId);
  var b=Amount(input.BaseAmount,9999999999999999.99m,true);
  var allowance=Amount(input.Allowances,9999999999999999.99m,true);
  var deduction=Amount(input.Deductions,9999999999999999.99m,true);
  if(b+allowance<deduction||input.EffectiveOn==default)throw new ArgumentException("Invalid salary.");
  return await store.Salary(a.TenantId,input,ct)??throw new HrScopeException();
 }
 public async Task<HrPayrollPreview> Preview(Guid campus,Guid employee,DateOnly month,CancellationToken ct)
 {
  var a=await Admin(ct);Id(campus);Id(employee);
  if(month==default||month.Day!=1)throw new ArgumentException("Payroll month must be first of month.");
  return await store.Preview(a.TenantId,a.SubjectId,campus,employee,month,ct)??throw new HrScopeException();
 }
 public async Task<HrDashboard> Dashboard(Guid campus,CancellationToken ct)
 {var a=await Admin(ct);return await store.Dashboard(a.TenantId,Id(campus),ct);}
}
