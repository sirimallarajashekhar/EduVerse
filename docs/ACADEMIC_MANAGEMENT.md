# EduVerse Phase 7 — Academic Management

## Module boundaries
All academic domain validation, authorization, persistence and entitlement decisions are backend-owned (.NET 10 Application and Infrastructure). MVC/Razor only serves views, and the BFF forwards allowlisted calls using the authenticated bearer token. Academic administration lives at `/admin/academics`.

The API uses the validated tenant claim and rechecks **active tenant ADMIN membership** and an **active academic subscription entitlement** on each call. Requests never accept a tenant ID. Every academic record requires a campus ID, and the database enforces tenant+campus composite foreign keys, blocking cross-tenant/campus relationships.

## MySQL schema — migration 005
After migrations 001 through 004, apply `database/migrations/005_academic_management.sql` using a separate privileged migration account.

- `academic_years`: tenant/campus, code, start/end dates.
- `academic_catalog`: school GRADES, CLASSES, SECTIONS and TERM; college PROGRAMS, COURSES, BATCHES, SEMESTERS; shared SUBJECTS. Parent relationships are validated by Application/Infrastructure and constrained by scoped FKs.
- `academic_faculty_assignments`: assign an active tenant member to a SUBJECT and academic year within the same campus. This is a faculty allocation structure, **not** an HRMS employee record.
- `academic_calendar_events`: holidays, exams, events and academic dates scoped to a campus/year.
- Migration also adds a scoped unique index to the existing institution department table so catalog department references cannot cross campuses.

The catalog uses unique codes per tenant/campus/kind. Codes are **not yet versioned per academic year**, so reusing the same section code for later years requires a future schema adjustment or a distinct code.

## REST endpoints
| Method | Path | Access |
|---|---|---|
| GET/POST | /api/v1/academic/years | Active tenant ADMIN with academics entitlement |
| GET/POST | /api/v1/academic/catalog | Active tenant ADMIN with academics entitlement |
| GET/POST | /api/v1/academic/faculty-assignments | Active tenant ADMIN with academics entitlement |
| GET/POST | /api/v1/academic/calendar | Active tenant ADMIN with academics entitlement |
| GET | /api/v1/academic/overview | Active tenant ADMIN with academics entitlement |

GET collection endpoints require `campusId` as query parameter. Catalog GET optionally accepts `kind` from the fixed academic-kind allowlist. POST uses typed JSON DTOs. Cross-campus/invalid relations return 404; input validation returns RFC 7807 400; duplicate codes/assignments return 409.

## Academic catalog rules
- TERM / SEMESTER / SECTION / BATCH require an academic year.
- COURSE must have a PROGRAM parent.
- BATCH must have a PROGRAM parent.
- SECTION must have CLASS or GRADE parent.
- CLASS may have a GRADE parent.
- SUBJECT may link to a CLASS, GRADE, COURSE or PROGRAM, or be standalone.
- SEMESTER may link to a PROGRAM.
- A supplied department must be active and in the same campus.
- Faculty assignments require a SUBJECT, academic year and an active institution member, all in the same tenant/campus context.
- Calendar dates and academic year date ranges are validated by services; calendar event dates need not fall completely inside the year yet (future stricter calendar policy).

## School/college distinctions
The shared model supports school grade/class/section organization and higher-education program/course/semester/batch organization. Individual institutions can be School, College or Mixed. Institution type-specific workflows and enrollment constraints are scheduled for later modules.

## UI and operational notes
Browse `/admin/academics` after signing in as a tenant ADMIN with an active PILOT or STANDARD subscription containing `academics`. Use the campus selector; the UI loads academic years, catalog, assignments and events via BFF and REST APIs. Institution departments/campuses and active user memberships must be provisioned through earlier phases.

This is an initial master-data CRUD foundation with create/list operations. Academic year rollover, timetable scheduling, curriculum versions, detailed faculty workload, section occupancy, update/deactivate workflows, student enrollments and exams remain later milestones.

## CI
GitHub Actions starts MySQL 8, applies migrations 001–005, builds all projects, tests API/tenant campus isolation, faculty assignments, academic hierarchy and Razor access control, then publishes deployables. Production secrets and production data are never committed.
