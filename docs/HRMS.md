# Phase 12: EduVerse HRMS independent micro frontend

## Architecture
The HRMS application at `/hrms` is independently deployable, hosted by `EduVerse.Hrms.Web` and mounted through the portal's YARP routing. The module is a **thin Razor/MVC UI** backed by an authenticated same-origin BFF `/hrms/bff/*`. The HRMS UI contains **no database access or domain logic**.

All HRMS business logic and authorization run in `EduVerse.Api` at `/api/v1/hrms/*`, backed by Application-layer `HrmsService` and tenant/campus-constrained MySQL repositories. The JWT supplies tenant identity; client-provided tenant IDs are never accepted. Every request requires an **active tenant ADMIN**, active session, active institution, and an HRMS feature subscription. Migration 010 enables `hrms` for PILOT in addition to STANDARD to support tests/pilot adoption.

## Migration
Apply `database/migrations/010_hrms.sql` after migrations 001–009 using a **migration-only database account**. Tables are:
- `hr_designations`: campus-scoped designation code and title.
- `hr_employees`: immutable unique employee number, optional institutional user identity and department/designation, lifecycle status.
- `hr_employee_events`: actor-attributed lifecycle audit history.
- `hr_employee_documents`: private PDF/PNG/JPEG files up to 1 MiB and content SHA-256 verification.
- `hr_leave_types`, `hr_leave_balances`, `hr_leave_requests`: calendar-year entitlements, dated requests and approval decisions.
- `hr_salary_structures`: effective-dated INR base, allowances and deductions.
- `hr_payroll_previews`: month-specific **calculations only**. No bank payments or statutory payroll processing.

## Endpoints
Authenticated `/api/v1/hrms`:
- `GET/POST /designations`, `GET/POST /employees`, `POST /employees/{id}/lifecycle?campusId=...`.
- `GET/POST /employees/{id}/documents?campusId=...`, `GET /employees/{id}/documents/{document}/download?campusId=...`.
- `GET/POST /leave-types`, `GET/POST /leaves`, `POST /leaves/{id}/decision?campusId=...`.
- `GET /employees/{id}/leave-balance?campusId=...&leaveTypeId=...&year=...`.
- `POST /salaries`; `POST /payroll/preview?campusId=...&employeeId=...&month=YYYY-MM-01`.
- `GET /attendance?campusId=...&date=...`; `GET /dashboard?campusId=...`.

The Razor BFF allows only these fixed operations. POST/HTML multipart requests require cookie authentication and anti-forgery tokens; outgoing API calls carry the authenticated user's bearer token.

## Leave correctness
- Requests must fit **one calendar year**, use inclusive **calendar days** and cannot overlap a pending/approved request for the same employee.
- During approval, a tenant-level InnoDB row lock serializes competing leave approvals. The calendar-year allowance is initialized from the leave type; approved days are added **only if the total cannot exceed the allowance**. Repeated approval/rejection returns 404. No negative balances.
- The leaves workflow currently requires an HR ADMIN to submit and approve. Employee self-service requests, delegated managers, half-day/hourly leave, carry-forward, holidays/weekends, comp-off and cancellation require future HRMS policy extensions.

## Attendance and payroll
- HRMS attendance reports read the existing Phase 9 staff attendance records for employees who have a linked `identity_users` account in the same tenant/campus. Attendance capture remains owned by Phase 9.
- Salary structures are effective dated and stored as `DECIMAL(18,2)`, currency INR. A payroll preview selects the latest effective structure and calculates `gross = base + allowances`, `net = gross - deductions`. Preview rows are overwritten for the same employee/month; no payroll ledger, approvals, bank transfer, payslips, TDS, PF, ESI or statutory compliance is claimed.
- Employees with no linked identity can still be onboarded; they will not appear in staff-attendance integration until linked.
- The HRMS dashboard summarizes employees, active staff, leave queues and preview totals. It does not expose individual salaries outside protected HRMS APIs.

## Privacy and production limits
HRMS includes identity documents, contract records and salary data. Production requires formal role segregation (HR admin vs payroll approver), MFA, authorization audit logging, encryption at rest, secure backups, access review, document malware scanning and retention/erasure policies. Initial private MySQL MEDIUMBLOB storage is intended for small pilot documents and should be replaced with encrypted private object storage for large workloads.

This initial milestone intentionally excludes attendance-to-payroll deduction calculations, timekeeping exceptions, payslip PDF generation, payout processing, statutory wage/tax calculations, detailed compensation components and multi-level leave approvals. Do not use payroll previews as finalized salary statements.

## CI
GitHub Actions applies MySQL migrations 001–010, builds and tests .NET 10, runs real MySQL/JWT HRMS integration tests (employment transitions, leave overlap/limits, payroll amounts, document scope and attendance integration), tests anonymous HRMS frontend restrictions and publishes API, Portal, AMS and HRMS independently.
