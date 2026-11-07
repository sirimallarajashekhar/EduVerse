# EduVerse Phase 11 — Exams & Results

## Architecture and security
API-first .NET 10 + MySQL 8. Exam setup, marks, approvals and publication are authorized by the API using the validated JWT tenant claim, live tenant **ADMIN** membership, active institution and academics subscription entitlement. Every foreign key to academic year, class, subject and enrollment is tenant/campus scoped. Razor at `/admin/exams` is presentation-only, backed by a same-origin authenticated and CSRF-protected BFF.

## Migration 009
Run `database/migrations/009_exams_results.sql` **after 001–008** using a privileged migration account. The API does not apply migrations on startup.

- `exam_grading_schemes` and `exam_grade_bands`: campus-specific grading scheme with **non-overlapping contiguous decimal percentage thresholds** from 0.00 to 100.00, integer/decimal grade points (0–10).
- `examinations`: academic-year exam type, scheme, DRAFT → APPROVED → PUBLISHED workflow.
- `exam_assessments`: subject-specific exams for school classes/sections/grades or college courses/batches, scheduled date, maximum and pass marks, credits.
- `exam_student_marks`: draft-only entries with unique enrollment+assessment keys, bounded marks and absent semantics.
- `exam_result_publications`: locked publication transaction produces persistent score, percentage, GPA and pass/fail summaries. Published totals do not change with later mark editing; editing is disallowed after approval.
- `exam_result_subject_snapshots`: immutable subject marks, grades, percentages and pass/fail entries per published report, retained across later student promotion and withdrawal.

## REST API
| Method | Endpoint | Description |
|---|---|---|
| GET/POST | `/api/v1/exams/schemes` | Create and list grading schemes |
| GET/POST | `/api/v1/exams` | Academic-year examinations |
| GET/POST | `/api/v1/exams/assessments` | Subject/date schedules and credits |
| GET/POST | `/api/v1/exams/marks` | Draft student marks |
| POST | `/api/v1/exams/{examId}/approve?campusId=...` | Lock marks after completeness check |
| POST | `/api/v1/exams/{examId}/publish?campusId=...` | Publish result snapshots transactionally |
| GET | `/api/v1/exams/{examId}/results/{enrollmentId}?campusId=...` | Published student report card |
| GET | `/api/v1/exams/{examId}/results/{enrollmentId}/download?campusId=...` | Download report card as UTF-8 text attachment |
| GET | `/api/v1/exams/{examId}/analytics?campusId=...` | Cohort average, pass %, highest/lowest |
| GET | `/api/v1/exams/students/{studentId}/cgpa?campusId=...` | Mean GPA over published exams |

## Invariants and calculations
- All marks use **decimal(8,2)** and C# `decimal`; no floating point or implicit rounding on entry.
- Present marks must be from zero to assessment maximum; absent marks must be NULL, never a disguised zero. Absence counts as zero for aggregate scores and is a failure even if the passing threshold is zero.
- Grades map the student percentage rounded to two decimals to exactly one configured band. Schemes must cover every hundredth percentage point with no overlap/gap.
- Percentage = `ROUND(SUM(marks)/SUM(max_marks) × 100, 2)`. GPA = `ROUND(SUM(grade_points × credits)/SUM(credits), 2)`. Passing requires meeting **each** subject's pass mark and attendance/presence.
- CGPA in this first version is **a simple average of published exam GPAs**, not a full credit-weighted semester transcript CGPA. Universities should configure institution-specific rules before relying on it.
- An exam cannot be approved without marks for **every active enrolled student** in each assessed class; approval blocks further mark edits and assessment changes. Publication writes result summaries inside one transaction.
- Result read/download and analytics require PUBLISHED state. These endpoints are currently **administrator-only**, not anonymous/public student results.
- Subject lines on report cards are read from published subject-level snapshots, rather than recomputed from mutable enrollment state. Student identity fields are still read from the student record.
- No class rankings are calculated in this phase. Analytics provides averages, pass rates, highest and lowest.

## Limitations / hardening
- School and college grading schemes are configurable, but credit-weighted final college CGPA, semester transcript aggregation, moderation, make-up exams, revaluation, transcript issuance, signed PDF report cards, faculty-specific mark permissions and guardian/student self-service are future phases.
- Manual exam scheduling does not reserve timetable rooms or prevent multiple exam-day conflicts. Add clash detection for campus exam calendars and subject/class combinations before broad release.
- Published results should be immutable; corrections need an auditable formal revaluation workflow, not direct SQL writes.
- Use MFA for result approvers, immutable audit logging, approval delegation, access monitoring, protected backups and secure retention before storing production-grade student results.
- No external examination board integrations.

## CI
CI starts MySQL 8, applies migrations 001–009, then builds .NET 10 solution, runs MySQL/JWT security/integration and Razor tests and publishes API, Portal, AMS and HRMS deployables.
