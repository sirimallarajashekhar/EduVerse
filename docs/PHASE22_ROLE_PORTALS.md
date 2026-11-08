# EduVerse Phase 22 — teacher, parent and student portals

## URLs
- Teacher: `/portal/teacher`
- Parent: `/portal/parent`
- Student: `/portal/student`

All screens are thin Razor/Bootstrap/JavaScript. The Web project uses a CSRF-protected, strictly allowlisted `/bff/portals` API proxy. Portal business rules, permission decisions and record ownership checks remain in the REST API.

## REST endpoints
Teacher:
- `GET /api/v1/portals/teacher/classes?campusId=...` lists timetable-assigned classes
- `GET /api/v1/portals/teacher/classes/{classId}/students?campusId=...` lists active assigned-class students and enrollment IDs
- `GET /api/v1/portals/teacher/lessons?campusId=...` returns lessons matching the teacher's assigned class/year/subject, with `learning.read`
- `POST /api/v1/portals/teacher/attendance` records a daily or period mark after an `attendance.manage` grant and assignment checks
- `POST /api/v1/portals/teacher/marks` records examination marks after an `exams.manage` grant and assignment checks

Parent:
- `GET /api/v1/portals/parent/children`
- `GET /api/v1/portals/parent/children/{studentId}/attendance|results|notices?campusId=...`

Student:
- `GET /api/v1/portals/student/me`
- `GET /api/v1/portals/student/{studentId}/attendance|results|timetable|assignments|notices?campusId=...`

Only an active `PARENT` or `SELF` `student_portal_links` relationship allows access to that student's own records. The `student_portal_links` table from migration 019 is reused; this phase has no new schema migration.

## Security guarantees
- All API requests need valid tenant-bound, active session authentication.
- Teacher write permission is verified in the application service via `TenantReadAuthorizer`, including the `academics` plan feature and assigned campus.
- **Inside the MySQL attendance and exam marks transaction**, MEMBERS must be active timetable teachers for the enrollment's current class and academic year; exam marks additionally require the assessment's subject to match the teacher's assignment. ADMIN maintains its preexisting write authority. The existing holiday/date/enrollment validation and draft-only exam rules still run.
- Parent/Student dashboards query only linked students, not general academic or student management routes. Cross-tenant, wrong-campus and wrong-relationship lookups return 404.
- Exam results are read only from `exam_result_publications` for examinations with `PUBLISHED` status. Draft exam marks are never exposed through parent/student endpoints.
- Student assignments require published lesson and assignment states; parent notices restrict audience to `ALL` or `GUARDIANS`. Students see only `ALL` notices.
- No parent access is inferred just from an email or browser-supplied student ID; institution ADMIN must establish the guardian relationship and verify identity.

## Provisioning
1. Institution admin creates a custom TEACHER role in `/admin/rbac`.
2. Assign `academic.read`, `students.read`, `learning.read`, `attendance.manage` and `exams.manage` for teaching duties.
3. Assign role to active MEMBER, grant campus access using `POST /api/v1/identity/campus-access`, and create a timetable assignment for the teacher/class/subject.
4. After permissions change, the teacher signs in again because RBAC changes revoke existing sessions.
5. For parents, create guardian contact information then approve a PARENT identity link via `POST /api/v1/ownership/links`. For students, verify identity before an admin creates a SELF link.

## Scope limitation
- Teacher lessons are **read-only** in the new portal; creation/publication stays institution-admin-controlled until teacher-specific content ownership and approval rules are added.
- Parent/student access is read-only; no private message write, assignment submission, document download, or finance access has been opened.
- The UI prompts teachers for an existing campus ID; institution campus selection / profile UX can be improved without altering authorization.
- Class/subject verification at transaction time protects against role scope escalation, but institution operators should audit old timetable assignments and subject provisioning.
