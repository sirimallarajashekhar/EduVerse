# Phase 21: Action-level RBAC, student ownership and teaching rosters

## API authorization

A fail-closed `RequireModuleActions` filter now sits on each major ERP API family: Academic, Students, Attendance, Exams, Finance, College, Learning, Operations, HRMS, AMS, Reporting and Workflows.

- Every request requires a validated JWT and current active tenant membership. `PLATFORM_SUPER_ADMIN` cannot access institution modules without an institution membership.
- Existing **institution ADMIN** requests retain current behavior and original service/plan/campus checks.
- Non-admin members can only use an **explicit allowlist of safe GET endpoints**. Each request must have `campusId`, a live permission such as `academic.read`, and an active campus that is assigned to that user through `identity_campus_access`.
- Other GETs (including payroll, marks, documents, private messages, payments and transcripts) and **all mutations** remain admin-only until a dedicated service-level, action-specific delegation policy is implemented. A `*.manage` permission by itself does **not** grant write access in this phase.
- Selected application read methods were updated to authorize delegated campus reads via `TenantReadAuthorizer`, while data queries retain the tenant/campus predicates enforced by their existing stores. Browser menus never make authorization decisions.
- Existing Finance and College feature subscription gates remain active. Neither custom roles nor module actions bypass subscription entitlement checks.

## Teacher access (read-only)

`GET /api/v1/ownership/my/teaching/classes?campusId={id}` enumerates timetable classes where the signed-in subject ID is the current assigned teacher. It requires `academic.read` and active campus access.

`GET /api/v1/ownership/my/teaching/classes/{classId}/students?campusId={id}` requires `students.read`, active campus access, a current timetable teacher assignment to the specific class **and** an active student enrollment matching that class and academic year. No general student list, marks, financial data or documents are returned.

These are explicit safe roster reads; they are not yet authorization for teachers to mark attendance or grades. That work requires assignment-aware write validation and is deliberately excluded.

## Student and parent access (read-only)

Migration `019_student_ownership.sql` creates `student_portal_links` with composite tenant+campus student foreign keys and tenant membership foreign keys. No automatic link is inferred from untrusted email or a profile field.

A tenant ADMIN can POST `/api/v1/ownership/links` with `{ "campusId": "...", "studentId": "...", "userId": "...", "relationship": "SELF" | "PARENT" }`. For PARENT, the user must be an active tenant member whose normalized login email matches a contact email in an existing `student_guardians` record for the exact student+campus. An admin must verify parental custody/identity and consent outside the application before linking. Links are transactionally audited.

`GET /api/v1/ownership/my/students` and `GET /api/v1/ownership/my/students/{studentId}?campusId=...` use only the authenticated subject and link table to return basic student summary fields. An arbitrary GUID is insufficient. Cross-campus and unlinked records respond with 404. **No parent edit, documents, grades, fees or messaging are exposed.**

## Migration and rollout

After merging, apply `database/migrations/019_student_ownership.sql` after 018. Configure the API and Web as usual; no new environment secrets are required. Assign custom roles in `/admin/rbac` and campus access through existing identity administration APIs before using delegated routes.

## Security follow-ups

- Implement record-class-aware teacher attendance/grade writes transactionally.
- Add read-only student/parent attendance and results **only after** adding row-by-row ownership enforcement in those services.
- Implement institution-managed portal-link revocation, guardian verification processes, and dedicated student/parent login/portals.
- Apply permissions and subscription policies to other legacy administration endpoints, and implement configurable write permissions without bypassing domain validation.
- Audit legacy `MEMBER`-wide permissions for accidental broad grants. Grant least privilege.

CI applies migrations 001-019 to MySQL 8, builds/publishes all apps and exercises role, campus, class roster, parent-link and tenant isolation tests.
