# Phase 23 — scoped learning workflows and communication

New API family: `/api/v1/role-learning`. The existing `/api/v1/learning` administration API is unchanged and the Web client continues using REST APIs exclusively.

## Roles and endpoints
- Teacher with `learning.manage`, active campus access and academic subscription: `POST /teacher/assignments` creates a DRAFT assignment only for a PUBLISHED lesson whose class/year/subject is currently assigned to that teacher.
- Teacher with `learning.read`: `GET /teacher/submissions?campusId=` lists assigned-class submissions only. `POST /teacher/grade` requires `learning.manage`, grade <= maximum, submitted state, and assigned class/subject inside the same MySQL write transaction.
- Verified student SELF link: `POST /student/submit` accepts only the linked student, an active enrollment, a published lesson and assignment, and a future deadline; duplicates are rejected. `GET /student/{studentId}/submissions?campusId=` returns only the SELF-linked student's submissions and feedback.
- Verified parent PARENT link: `POST /parent/conversations` opens a thread for that student after checking the guardian email and binding inside the transaction. `GET /conversations?campusId=`, `GET /conversations/{id}/messages?campusId=`, `POST /conversations/reply` enforce parent verified contact/child relationship, current assigned classroom teacher, or tenant ADMIN; messages never leak to arbitrary campus users.
- Institution ADMIN: `GET /admin/reviews?campusId=` returns DRAFT lessons/assignments. `POST /admin/reviews/LESSON/publish` and `/ASSIGNMENT/publish` approve content. Assignments require a published lesson and a future deadline. All approvals are audited.
- In-app notification rows are produced for newly opened parent threads (assigned teachers), and parent accounts on replies. No SMS/email or push delivery is claimed.

## Web
`/portal/teacher`, `/portal/student`, `/portal/parent` gain forms for assigned-class workflows. `/portal/learning-review` is an ADMIN-only Razor approval queue, linked on the administrator home screen. All new JS calls go through CSRF-protected `/bff/role-learning` with an exact route allowlist. HTML is populated using `textContent` to avoid untrusted HTML insertion.

## Rollout
No new MySQL migration: existing migrations 001–019 provide the tables and constraints. Deploy API and Web from merged main, create RBAC teacher permissions and active timetable assignments, link student/parent profiles using the existing administrator approval workflow, then re-login after role changes.

## Deliberate limitations
- Parent conversation eligibility requires a verified guardian record and a current PARENT portal link, not a browser-supplied student ID alone.
- Graded submissions may not be re-graded from the role portal; future appeal/review workflows must be separately authorized.
- Approvals are publish-only; explicit rejection, revision requests and review comments are future enhancements.
- Monitoring and audit reporting currently use the admin review queue and existing identity audit records, not a new cross-tenant messaging oversight API.
