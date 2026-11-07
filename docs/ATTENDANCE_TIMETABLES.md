# Phase 9 — Attendance and Timetables

## Overview
Implements ASP.NET Core 10 REST API-owned academic periods, teacher allocation, class/section timetables, student daily + period-wise marks, staff-member attendance foundation and daily student attendance summaries. The thin Razor application lives at `/admin/attendance` and talks to the API through a same-origin anti-forgery-protected BFF.

## Migration 007
Apply `database/migrations/007_attendance_timetables.sql` after migrations 001–006 with a dedicated migration user. Production migrations do not run at API startup.

- `academic_periods`: campus-scoped teaching periods with start/end times.
- `academic_timetable_slots`: academic-year, class/section/course/batch, subject, period, weekday, and active teacher.
- `student_attendance`: one mark per student/date/DAILY or per student/date/PERIOD ID; stores enrollment and marker.
- `staff_attendance`: institution-member daily attendance foundation, campus-scoped.
- All relations are tenant/campus scoped where applicable. `attendance_key` avoids MySQL nullable UNIQUE behavior accidentally allowing duplicate DAILY marks.

## REST APIs (all require active tenant ADMIN + academic subscription entitlement)
| Method | Route |
|---|---|
| GET/POST | `/api/v1/attendance/periods` |
| GET/POST | `/api/v1/attendance/timetable` |
| GET/POST | `/api/v1/attendance/students` |
| GET | `/api/v1/attendance/students/summary` |
| GET/POST | `/api/v1/attendance/staff` |
| GET | `/api/v1/attendance/dashboard` |

GET endpoints accept `campusId`; timetable additionally requires `academicYearId`; student marks/summary require `enrollmentId`. Student marks optionally filter by `date`; staff GET requires `date`. POST endpoints accept typed request DTOs; tenant identity always comes from verified JWT claims, never request body.

## Important business rules
- A timetable allocation requires an active teacher with a valid academic faculty assignment for the **same subject and year**, and active academic year, course/section/class and period in the same campus.
- Conflict checks compare actual time intervals, not just identical period identifiers. A tenant row is locked before the campus row to serialize concurrent timetable writes across campuses. A teacher cannot be assigned overlapping times across campuses when academic year date ranges overlap, and a class cannot be assigned overlapping periods in its campus/year.
- Daily attendance must reference an active student enrollment whose academic year includes the date.
- Period-wise attendance additionally requires a timetable slot matching that enrollment's class, academic year, requested period and date's weekday.
- School/college HOLIDAY calendar events block student and staff attendance; no bypass through the client.
- Repeated student/staff submissions for a date and scope update the mark rather than creating duplicates. Correction audit history is **not** yet implemented; future releases should introduce immutable change logs and approval workflows.
- Daily attendance summary counts only DAILY marks, excluding PERIOD marks to avoid mixing two denominators. EXCUSED and LATE count as attended in the example percentage calculation; this policy may be configurable by institution later.
- Days of week are ISO-style: Monday=1 through Sunday=7.
- Staff attendance refers to an **active institution member**, not a future HRMS employment entity. HRMS integration, leave approval and payroll reconciliation are future work.

## Current restrictions
- Master-data CRUD is create/list. Rescheduling, cancellation, substitutes, annual rotation and complex recurring holidays remain future functionality.
- Only tenant ADMIN can currently mark attendance. Teacher-specific permission scoping and attendance delegation should be added as granular permissions later.
- Scheduling locks are tenant-wide to prevent a shared teacher being double-booked across campuses; this can reduce write concurrency for large institutions. School holiday checks remain campus-scoped. Assess multi-campus time zones, travel times and rescheduling policies before production use.
- Enrollment joins are scoped to campus but remain dependent on the current Phase 8 enrollment data integrity. Batch attendance entry, bulk imports, offline sync, timezone-specific cutoff rules and biometric hardware ingestion are not covered.
- Access to records is restricted; for real student data, use appropriate consent, retention, encrypted backups, audit and data governance policies.

## Tests and CI
CI runs MySQL 8 migrations 001–007 and .NET 10 build/tests/publish checks. Integration tests use actual JWTs, MySQL data and independent tenants to validate: overlap conflicts, school holidays, active student placement, repeated marks, staff member checks, role/tenant boundaries and liveness.
