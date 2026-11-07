using EduVerse.Contracts;
using EduVerse.Domain;
namespace EduVerse.Application.Hrms;
public interface IHrmsStore
{
 Task<bool> IsAdminAsync(TenantIdentity actor,CancellationToken ct);
 Task<HrDesignation?> Designate(Guid tenant,HrDesignationCreate input,CancellationToken ct);
 Task<IReadOnlyList<HrDesignation>> Designations(Guid tenant,Guid campus,CancellationToken ct);
 Task<HrEmployee?> Hire(Guid tenant,string actor,HrEmployeeCreate input,CancellationToken ct);
 Task<IReadOnlyList<HrEmployee>> Employees(Guid tenant,Guid campus,CancellationToken ct);
 Task<bool> Lifecycle(Guid tenant,string actor,Guid campus,Guid employee,HrLifecycle input,CancellationToken ct);
 Task<HrDocument?> Upload(Guid tenant,Guid campus,Guid employee,string type,string filename,string mime,byte[] content,CancellationToken ct);
 Task<IReadOnlyList<HrDocument>> Documents(Guid tenant,Guid campus,Guid employee,CancellationToken ct);
 Task<(string FileName,byte[] Bytes)?> Download(Guid tenant,Guid campus,Guid employee,Guid document,CancellationToken ct);
 Task<HrLeaveType?> LeaveType(Guid tenant,HrLeaveTypeCreate input,CancellationToken ct);
 Task<IReadOnlyList<HrLeaveType>> LeaveTypes(Guid tenant,Guid campus,CancellationToken ct);
 Task<HrLeave?> RequestLeave(Guid tenant,HrLeaveCreate input,CancellationToken ct);
 Task<IReadOnlyList<HrLeave>> Leaves(Guid tenant,Guid campus,CancellationToken ct);
 Task<bool> DecideLeave(Guid tenant,string actor,Guid campus,Guid leave,string decision,CancellationToken ct);
 Task<HrLeaveBalance?> Balance(Guid tenant,Guid campus,Guid employee,Guid type,int year,CancellationToken ct);
 Task<HrSalary?> Salary(Guid tenant,HrSalaryCreate input,CancellationToken ct);
 Task<HrPayrollPreview?> Preview(Guid tenant,string actor,Guid campus,Guid employee,DateOnly month,CancellationToken ct);
 Task<HrDashboard> Dashboard(Guid tenant,Guid campus,CancellationToken ct);
}
public sealed class HrScopeException:Exception {}
public sealed class HrConflictException(string message):Exception(message) {}
