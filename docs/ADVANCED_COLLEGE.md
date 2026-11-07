# Phase 16 — Advanced College Management

## API-first module

ASP.NET Core 10, MySQL 8, independent thin Razor/Bootstrap Portal screen `/admin/college` and its CSRF-protected bearer BFF `/bff/college`. All business rules, permissions, grade calculations, registration capacity and persistence belong to `EduVerse.Api`, `EduVerse.Application.College` and `EduVerse.Infrastructure.MySql`. The UI never accesses MySQL.

Run `database/migrations/014_advanced_college.sql` after migrations 001–013 using a migration account. Readiness checks for migration 014; CI applies 001–014.

## Modeling and integration
- `college_regulations`: passing points (0–10), minimum earned credits for progression, whether backlogs block progression, and maximum semester count.
- `college_programs`: extends a tenant/campus-scoped existing `academic_catalog` **PROGRAM** rather than duplicating the underlying academic hierarchy; binds to an explicit regulation.
- `college_semesters`: extends existing academic **SEMESTER** catalog and **academic_years**, tied to one degree program. Semester ordinal must be no greater than regulation maximum.
- `college_courses`: program/semester-specific offering for an existing **SUBJECT**, CORE or ELECTIVE, with credit weight, seat capacity, optional prerequisite from an *earlier semester of the same program*.
- `college_registrations`: a student with an active existing `student_enrollments` record in the same year and parent academic PROGRAM registers courses. Tenant-row InnoDB locks serialize competing seat requests, and the API checks prerequisites and **finalized previous semester eligibility**. No cross-campus or cross-program registrations.
- `college_semester_results`: immutable finalization snapshot per academic enrollment and semester, capturing attempted credits, earned credits, weighted points, SGPA, remaining backlog count and progression eligibility.
- Existing Phase 11 exam result **subject snapshots** are the sole permitted grade source. The grade import endpoint verifies a PUBLISHED **SEMESTER or FINAL exam**, exact student enrollment, semester academic year, and matching subject. It locks and posts the snapshot grade points, label and pass status into the registered course. No manual GPA entry or overwriting a posted grade.
- `PASSED` requires both the published exam subject's pass flag and college regulation's passing-point threshold. `BACKLOG` earns no credits. Both pass and fail grade points contribute to attempted-credit-weighted SGPA and CGPA.
- Finalization requires **all mandatory CORE offerings registered**, at least one registered/graded course and *no pending grades*. SGPA = sum(credits × grade points)/attempted credits, rounded to two decimals. Promotion eligibility requires minimum earned credits and either no backlogs or regulation permission.
- Transcript CGPA is recomputed as sum(finalized weighted points)/sum(finalized attempted credits) across all semesters of that **student and program**, not the older Phase 11 unweighted exam CGPA. Finalized semester snapshots are immutable to ordinary API calls.

## REST API

All endpoints require authenticated live tenant ADMIN plus active academic subscription. No client-provided tenant IDs are accepted, and all record references are scoped by tenant and campus.

`/api/v1/college`:
- GET/POST `/regulations`, `/programs`, `/semesters`, `/courses` (GET filtered by campusId)
- POST `/registrations`, GET `/registrations?campusId=...&enrollmentId=...`
- POST `/grades/import` with `{campusId, registrationId, examId}`
- POST `/semesters/finalize` with `{campusId, enrollmentId, semesterId}`
- GET `/transcripts?campusId=...&enrollmentId=...`
- GET `/report?campusId=...`: programs, semesters, courses/electives, registrations, backlogs, finalized terms and mean SGPA

## Known constraints and next work
This is a college-administration foundation, not a complete registrar/SIS. It uses published exam snapshots and provides *single attempt, immutable grade import*. Formal revaluation, exam retake reconciliation/backlog clearance, elective credit-basket constraints, transfer credits, cross-institution transcript recognition, graduating eligibility, timetable clashes and PDF grade sheets require subsequent workflows. Semester progression uses the immediately previous semester's **finalized eligibility**. The catalog academic year binds a student's `student_enrollments` record; year-to-year college progression requires the existing student reenrollment process.

Before a college production rollout, implement separate registrar/department-head/faculty roles, exam board approval, transcript signature and audit logging, automated catalog mapping checks, versioned academic regulations, and a formal reattempt/revaluation ledger. College result finalization must not be presented as an accredited university transcript until institution policies are configured and verified.

## CI
CI applies MySQL migrations 001–014, builds .NET 10, runs integration/security tests against real MySQL (seat limits, tenant isolation, exam-driven SGPA/CGPA, backlogs, progression) and publishes Portal/API/AMS/HRMS.
