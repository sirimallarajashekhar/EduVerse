# EduVerse Phase 8 — Admissions and Student Management

## Architecture
ASP.NET Core 10 + MySQL 8, API-first and multi-tenant. Razor administration UI at `/admin/students` and public applicant form at `/apply?campusId=<id>`. The BFF forwards allowlisted requests to the REST API using a protected login ticket. All student PII, guardian data, admission decisions, documents and lifecycle actions are restricted to an authenticated **active tenant ADMIN** with active core plan entitlement; future academic office RBAC is separate work.

## Migration
Apply `database/migrations/006_admissions_students.sql` after 001 through 005, using an operator migration credential (never from API startup).
Tables:
- `admission_applications`: immutable receipt number, SUBMITTED → APPROVED/REJECTED → ENROLLED. A repeated decision or cross-campus approval cannot succeed.
- `students`: globally unique UUID ID and `EV-{UUID}` admission number, unique per tenant. Applicant identity must match an approved application when registering from one.
- `student_guardians`: parent/guardian details, primary contact.
- `student_enrollments`: class/section/grade/course/batch placements referencing academic year and item in the **same tenant and campus**, one enrollment per year per student.
- `student_lifecycle_events`: audit entries for PROMOTED, TRANSFERRED, WITHDRAWN, including actor and destination campus.
- `student_documents`: private small files, SHA-256 verification, file categories and PENDING/VERIFIED/REJECTED states.

## REST API
Public submission is rate-limited `POST /api/v1/admissions/apply` and returns **only an application receipt**. The campus ID comes from an institution-provided application URL. No anonymous application browsing, approvals, student records, guardian details or document retrieval.

Authenticated `/api/v1/students`:
- `POST/GET /applications`: submit and list applications.
- `POST /applications/{id}/decision?campusId=...`: approve/reject once.
- `POST /, GET /?campusId=..., GET /{id}?campusId=...`: register and list/read student.
- `POST/GET /{id}/guardians?campusId=...`: add/list guardians.
- `POST/GET /{id}/enrollments?campusId=...`: add/list academic placement.
- `POST /{id}/lifecycle?campusId=...`: PROMOTE (new academic year/item), TRANSFER (records destination and closes source), WITHDRAW (closes source); all transactional.
- `POST/GET /{id}/documents?campusId=...`: multipart upload and protected listing.
- `GET /{id}/documents/{document}?campusId=...`: private download with `application/octet-stream` and attachment disposition to prevent inline execution.
- `POST /{id}/documents/{document}/review?campusId=...`: verify/reject a pending document.

## School & college workflows
School students enroll in GRADE, CLASS, SECTION; college students enroll in COURSE, BATCH (with academic year). Promotion requires a new academic year and allowed academic item. The record is retained when withdrawn/transferred and all original documents remain campus-scoped. For transfers, **no automatic cross-campus enrollment or record-copying** is performed: the source record is closed and transfer destination is audited. Destination campus onboarding requires a separate authorized action; no cross-tenant transfers.

## File privacy and security
Document uploads are limited to 1 MiB per file. Allowlisted PDF, PNG and JPEG content types must match file signatures, metadata is validated, files are stored in private MySQL MEDIUMBLOB columns and never under a public web root. Only the tenant's active admin can list, download or review. Avoid uploading Aadhaar/identity data until organizational privacy, retention, parental consent, encryption-at-rest and compliance practices are established. For production scale migrate blobs to private encrypted object storage, scan for malware, enforce quotas, backups and retention/deletion schedules.

## Deployment hardening
- Online admissions are rate-limited by client IP; implement applicant email/phone verification, CAPTCHA and abuse detection before public deployment. No guardian self-service account or application status tracker is provided in this phase.
- Admin authentication and authorization are rechecked by the REST API, not JS.
- Enrollment capacity, document malware scanning, admissions workflow notifications, transfers that auto-enroll students at destinations, demographic/medical records, and bulk import are not part of this initial lifecycle foundation.
- Always use HTTPS, least privilege DB accounts, encrypted storage, audit monitoring and secure backups for student PII.

## Testing
GitHub CI runs MySQL 8 migrations 001–006 then API, portal and app unit/security tests. Phase 8 includes application approval/registration, unique numbers, tenant/campus isolation, guardian and enrollment creation, document upload/verification/download, withdrawal, public anti-forgery and anonymous-access checks.
