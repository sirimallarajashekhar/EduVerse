# EduVerse Phase 15 — Learning & Communication

## Deployment
ASP.NET Core 10 modular-monolith API; independent Portal ASP.NET Core MVC/Razor + Bootstrap/jQuery. Only `EduVerse.Api` contains business logic, validation, calculation, authorization and MySQL operations.

Apply `database/migrations/013_learning_communication.sql` **after 001–012** using migration-only credentials. Do not let the API migrate on startup. The readiness check requires migration 013.

Security: API checks the validated JWT session, tenant active status, live ADMIN membership and an active academic-feature subscription for all new endpoints. All references require same tenant and campus; there is no caller-provided tenant identity. Browser controllers use cookie ADMIN plus same-origin anti-forgery protection and forward a real bearer token to the API. The first version uses staff-admin data entry, not guardian/student direct access.

## Modules and endpoints

All routes below are under `/api/v1/learning` (and the BFF mirrors them under `/bff/learning`).

| Method | Path | Rule |
|---|---|---|
| GET/POST | `/lessons` | Create and list draft lessons for active academic year, class/course and subject |
| POST | `/lessons/{id}/publish?campusId=` | DRAFT → PUBLISHED |
| GET/POST | `/assignments` | Published lesson required; UTC due date, max score |
| POST | `/assignments/{id}/publish?campusId=` | DRAFT → PUBLISHED before deadline |
| GET/POST | `/submissions` | Published assignment, active enrollment in same academic year/class; one submission per assignment/enrollment |
| POST | `/submissions/{id}/grade?campusId=` | Score must not exceed max; single, locked grading event |
| GET | `/enrollments/{id}/progress?campusId=` | Submitted, graded counts and mean percent on graded work |
| GET/POST | `/announcements` | Publish an audience-tagged campus announcement |
| GET/POST | `/threads` | Parent–teacher record for actual student+guardian relationship |
| GET/POST | `/messages` and `/threads/{id}/messages` | Administrator-authored campus messages |
| GET | `/notifications?campusId=` | Signed-in staff admin's in-app inbox |
| POST | `/notifications/{id}/read?campusId=` | Mark own delivered notification read once |
| GET | `/dashboard?campusId=` | Learning and communication overview |

### Learning invariants
- Lesson must reference existing active year, school class/section/grade or college course/batch, and an active subject within the same campus. Only HTTPS resource links are accepted. Content is text plus external links; attachments/streamed multimedia are not stored in the application.
- Assignments reference published lessons. Max score is a two-decimal `DECIMAL(8,2)`, strictly greater than zero; due date is a future UTC instant.
- Submissions require a current ACTIVE student, active enrollment matching the lesson's year and class, and a PUBLISHED assignment before its deadline. Duplicate submissions fail with HTTP 409 and cannot overwrite graded answers.
- Grading is transactional: score 0..maximum, two decimal places, actor attribution and feedback. A second grading attempt cannot mutate the original grade without a future regrading workflow. A score is not a formal examination result and is not sent to Phase 11 exams.
- Progress is a read-only aggregate of published assignments visible to the specified enrollment; average is computed only from graded submissions.

### Communication and privacy
- Announcements are campus-scoped with audience labels `ALL`, `STAFF` and `GUARDIANS`. A notification row is transactionally inserted into **in-app** inboxes of active institutional account members according to the selected audience. For `GUARDIANS`, the current implementation creates an administrator-facing record only, because guardians lack authenticated EduVerse accounts. **Do not claim parent delivery.**
- Parent–teacher threads are administrator-mediated and require a guardian record linked to the specified student in the same campus. Messages carry the signed-in administrator's ID. Guardian login and teacher responses are future Phase 16+ identity/workflow work.
- `DELIVERED` means the item was saved in a staff user's in-app inbox, not that a person opened or saw it. `READ` is explicitly recorded through a scoped read-receipt request; no email, SMS, WhatsApp or mobile push is sent.
- Inbox access is scoped to the signed-in account; cross-campus/cross-tenant IDs cannot retrieve or mark another account's message.
- In this initial version, attachments, outbound channels, subscription opt-outs, scheduled publishing, direct parent messaging, class-scoped announcements, retention/erasure controls and moderation are deferred.

## Tests and production hardening
- Real MySQL 8 / JWT integration tests validate lesson state, submission enrollment scope, duplicate keys, score limits, immutable grading, progress, guardian relationship, announcement delivery, read receipts, unauthorized users and tenant/campus isolation.
- Portal tests cover unauthenticated administration, BFF message access and CSRF.
- CI applies schema 001–013, restores, builds, runs all tests and publishes API, Portal, AMS and HRMS independently.

Before production with children’s records and communications: add explicit parent/guardian consent, teacher-specific RBAC, child privacy policies, retention, audit logs, content filtering, rate limits and secure malware-scanned uploads.
