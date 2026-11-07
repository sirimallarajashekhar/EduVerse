namespace EduVerse.Contracts;

public sealed record FeeStructureCreate(Guid CampusId,Guid AcademicYearId,string Code,string Title,decimal Amount);
public sealed record FeeStructureRecord(Guid Id,Guid CampusId,Guid AcademicYearId,string Code,string Title,decimal Amount,string Currency);
public sealed record InstallmentInput(int Number,DateOnly DueOn,decimal Amount);
public sealed record ChargeCreate(Guid CampusId,Guid EnrollmentId,Guid FeeStructureId,IReadOnlyList<InstallmentInput> Installments);
public sealed record ChargeRecord(Guid Id,Guid CampusId,Guid StudentId,Guid EnrollmentId,Guid FeeStructureId,decimal Gross,decimal Concessions,decimal Paid,decimal Refunded,decimal Balance);
public sealed record InstallmentRecord(Guid Id,Guid ChargeId,int Number,DateOnly DueOn,decimal Amount,decimal Balance);
public sealed record ConcessionCreate(Guid CampusId,Guid ChargeId,string Category,string Reason,decimal Amount);
public sealed record ConcessionRecord(Guid Id,Guid ChargeId,string Category,string Reason,decimal Amount);
public sealed record PaymentCreate(Guid CampusId,Guid InstallmentId,decimal Amount,string Method,string? Reference);
public sealed record PaymentRecord(Guid Id,Guid CampusId,Guid ChargeId,Guid InstallmentId,decimal Amount,string Method,string ReceiptNumber,string ReconciliationStatus);
public sealed record RefundCreate(Guid CampusId,Guid PaymentId,decimal Amount,string Reason,string Reference);
public sealed record RefundRecord(Guid Id,Guid PaymentId,decimal Amount,string Reference);
public sealed record ReconcileRequest(Guid CampusId,Guid PaymentId,string Status,string? Reference);
public sealed record ReceiptRecord(string ReceiptNumber,string Institution,string Student,string AdmissionNumber,string Fee,
    decimal Amount,string Currency,DateTime CollectedUtc,string Method,string? Reference);
public sealed record FinanceDashboard(int Fees,int Charges,int Payments,decimal Gross,decimal Concessions,decimal Collected,decimal Refunded,decimal Outstanding,decimal Overdue);
