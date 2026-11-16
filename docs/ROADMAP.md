# EduVerse ERP — Phase-wise Roadmap

**Architecture:** .NET 10 REST APIs, MySQL 8, thin ASP.NET Core MVC/Razor + Bootstrap + JavaScript UI, YARP route-based micro frontends for independently deployable modules. No Angular. Every UI module calls its own module API; all business rules, authorization and persistence are backend-owned. Tenant isolation and scalability are architectural requirements.

**Workflow:** main is the default branch. Use feature branches, PR reviews, CI checks, migration reviews and acceptance tests. Checkboxes reflect verified implementation as of 16 November 2026. Checked means the stated development scope is implemented and covered by repository tests/CI; it does not certify deployment or production readiness. An unchecked item may be partially implemented; the remaining scope is recorded below.

## Phase 01 — Project Setup & GitHub

- [x] **P01-T01** Create EduVerse.slnx and source structure
- [x] **P01-T02** Define main and feature branch workflow
- [x] **P01-T03** Add coding conventions and repository documentation
- [x] **P01-T04** Configure GitHub Actions build checks
- [x] **P01-T05** Create baseline testing projects

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 02 — Architecture & Portal Shell

- [x] **P02-T01** Define bounded module ownership and REST contracts
- [x] **P02-T02** Build MVC/Razor Portal Shell and Bootstrap design system
- [x] **P02-T03** Configure YARP route-based micro frontends
- [x] **P02-T04** Implement module navigation and feature discovery
- [x] **P02-T05** Specify module deployment/version contracts

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 03 — Multi-Tenant Database

- [x] **P03-T01** Design institutions, campuses and tenant data model
- [x] **P03-T02** Create MySQL migrations, indexes and constraints
- [x] **P03-T03** Implement trusted tenant resolution
- [x] **P03-T04** Enforce tenant isolation in queries and services
- [x] **P03-T05** Automate cross-tenant security tests

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 04 — Identity & Permissions

- [x] **P04-T01** Configure centralized authentication and SSO
- [x] **P04-T02** Implement roles, claims and permission policies
- [x] **P04-T03** Build login, logout and account recovery
- [x] **P04-T04** Add session controls and audit trail
- [x] **P04-T05** Test authorization by tenant and role

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 05 — API Standards & Scalability

**Remaining / partial:** Pagination/filtering is partial across modules. Load and horizontal-scaling scripts need measured acceptance results.

- [x] **P05-T01** Standardize API versioning, validation and errors
- [ ] **P05-T02** Implement pagination, sorting and filtering
- [x] **P05-T03** Configure health checks and observability
- [x] **P05-T04** Introduce idempotency and background work conventions
- [ ] **P05-T05** Create load and horizontal-scaling tests

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 06 — Institution Administration

- [x] **P06-T01** Build institution registration and onboarding
- [x] **P06-T02** Configure schools, colleges and campuses
- [x] **P06-T03** Manage departments, buildings and institution settings
- [x] **P06-T04** Implement subscriptions and feature access
- [x] **P06-T05** Create institution administration screens

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 07 — Academic Management

**Remaining / partial:** Subjects, course credits and regulations exist; broader curriculum lifecycle/versioning remains partial.

- [x] **P07-T01** Manage academic years, classes and sections
- [x] **P07-T02** Configure departments, programs and semesters
- [ ] **P07-T03** Manage subjects, credits and curricula
- [x] **P07-T04** Implement teacher/faculty assignments
- [x] **P07-T05** Build academic calendar and timetable APIs

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 08 — Admissions & Students

**Remaining / partial:** Admission applications are implemented; a separate enquiries workflow is not. Admission numbers and document upload/download exist; generated ID cards and broader academic documents remain partial.

- [ ] **P08-T01** Implement enquiries and admission applications
- [x] **P08-T02** Configure admission approvals and enrollment
- [x] **P08-T03** Manage student and guardian profiles
- [x] **P08-T04** Implement promotions, transfers and withdrawals
- [ ] **P08-T05** Generate student ID and academic documents

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 09 — Attendance & Timetables

**Remaining / partial:** Daily and period attendance exist. Substitutions, formal corrections/approvals and shortage reporting remain partial.

- [x] **P09-T01** Build student attendance workflows
- [x] **P09-T02** Implement subject-wise college attendance
- [ ] **P09-T03** Manage periods, substitutions and schedules
- [ ] **P09-T04** Implement corrections and approvals
- [ ] **P09-T05** Generate attendance and shortage reports

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 10 — Fees & Finance

**Remaining / partial:** Payments/receipts/refunds are manual/offline. Invoicing, provider reconciliation, approval chains and full accounting audit/report scope remain partial.

- [x] **P10-T01** Configure fees, installments and concessions
- [ ] **P10-T02** Implement invoices and student balances
- [x] **P10-T03** Implement payments, receipts and refunds
- [ ] **P10-T04** Build reconciliation and approval workflows
- [ ] **P10-T05** Create financial reports and audit logs

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 11 — Exams & Results

**Remaining / partial:** School grades, downloadable report cards, semester grades and SGPA/CGPA exist. Report-card downloads are plain text; richer PDF layouts and certificate generation remain pending.

- [x] **P11-T01** Configure examination types and schedules
- [x] **P11-T02** Build marks entry and moderation workflows
- [x] **P11-T03** Calculate school grades and report cards
- [x] **P11-T04** Implement semester grades, SGPA and CGPA
- [ ] **P11-T05** Publish results, transcripts and certificates

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 12 — HRMS Micro Frontend

**Remaining / partial:** Employee/faculty records, leave and salary previews exist. Recruitment, shifts, finalized payroll, payslips, statutory processing, appraisals and self-service remain partial or pending.

- [x] **P12-T01** Build independent HRMS Razor frontend and API
- [ ] **P12-T02** Implement employee, faculty and recruitment data
- [ ] **P12-T03** Build staff attendance, shifts and leave
- [ ] **P12-T04** Implement payroll, payslips and statutory settings
- [ ] **P12-T05** Add appraisal, employee self-service and exits

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 13 — AMS Micro Frontend

**Remaining / partial:** Assets, inventory movements, draft purchases and disposal transitions exist. Purchase approvals/receipts, warranty/QR, depreciation and valuation reporting remain partial.

- [x] **P13-T01** Build independent AMS Razor frontend and API
- [ ] **P13-T02** Implement procurement, categories and asset register
- [x] **P13-T03** Manage allocation, transfers and returns
- [ ] **P13-T04** Implement maintenance, warranty and QR verification
- [ ] **P13-T05** Build inventory, depreciation and disposal reports

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 14 — Library Transport Hostel

**Remaining / partial:** Library loans/returns/fines, routes and hostel allocations exist. Full fee integration and combined operations reporting remain partial.

- [x] **P14-T01** Implement library catalog and circulation
- [x] **P14-T02** Build loans, returns and fine handling
- [x] **P14-T03** Manage transport vehicles, routes and allocations
- [x] **P14-T04** Manage hostel rooms, beds and allocations
- [ ] **P14-T05** Integrate fees and operations reports

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 15 — Learning & Communication

**Remaining / partial:** Student/parent/teacher workflows and in-app notifications exist. Outbound SMS/WhatsApp delivery, full template management and delivery history remain pending.

- [x] **P15-T01** Implement homework, materials and submissions
- [x] **P15-T02** Build student and parent portal interfaces
- [x] **P15-T03** Manage announcements and circulars
- [ ] **P15-T04** Integrate SMS, email and WhatsApp providers
- [ ] **P15-T05** Track templates, notifications and delivery history

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 16 — Advanced College

**Remaining / partial:** Credit-based registration, prerequisites, SGPA/CGPA and progression exist. Internships/placements, research/alumni and accreditation/graduation scope remain pending.

- [x] **P16-T01** Build course registration and electives
- [x] **P16-T02** Enforce prerequisites and credit requirements
- [ ] **P16-T03** Manage internships and placements
- [ ] **P16-T04** Track research, publications and alumni
- [ ] **P16-T05** Implement graduation and accreditation reporting

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 17 — Cross-Module Workflows

**Remaining / partial:** Identity binding, clearance/offboarding, finance-handoff contracts and internal outbox receipts exist. Finance posting, external delivery and complete school/college acceptance evidence remain pending.

- [x] **P17-T01** Connect HRMS onboarding with Identity
- [x] **P17-T02** Connect HRMS offboarding with AMS clearance
- [x] **P17-T03** Implement finance/procurement integration contracts
- [x] **P17-T04** Introduce reliable messaging and outbox processing
- [ ] **P17-T05** Test school and college cross-module workflows

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 18 — Reporting Security Quality

**Remaining / partial:** Dashboards, PDF/Excel exports and automated tests exist. Scheduled reporting, accessibility, measured performance/security/resilience assessments and larger-data acceptance remain pending. Issue #18 stays open.

- [x] **P18-T01** Build administrative analytics dashboards
- [ ] **P18-T02** Implement PDF, Excel and scheduled reports
- [x] **P18-T03** Automate unit, integration and UI tests
- [ ] **P18-T04** Run tenant isolation and accessibility assessments
- [ ] **P18-T05** Run performance, security and resilience tests

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 19 — Deployment & Pilot

**Remaining / partial:** Release/deployment pipeline code and operational scripts exist. Actual deployment, backup/restore drills, independent alerts, training and pilot sign-off are not established by CI. Issue #19 stays open.

- [x] **P19-T01** Implement module-specific CI/CD pipelines
- [ ] **P19-T02** Provision secrets, MySQL and backups
- [ ] **P19-T03** Deploy Portal, core APIs, HRMS and AMS
- [ ] **P19-T04** Conduct school/college pilot and user training
- [ ] **P19-T05** Complete monitoring and production runbooks

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Common quality gates

- Module-specific API ownership; no UI business logic or direct database access.
- Tenant and permission enforcement for every protected API.
- Automated tests and secure defaults; secrets outside source control.
- Stateless deployment and observable operations.
- Use representative synthetic data in tests; never commit real student, employee or payroll records.

## Verification references

- Foundation/identity/tenancy: `tests/EduVerse.Api.Tests/FoundationApiTests.cs`, `IdentityAuthenticationTests.cs`, `TenantIsolationTests.cs` and `AdvancedRbacIntegrationTests.cs`.
- Module coverage: the matching `AcademicManagementTests`, `StudentManagementTests`, `AttendanceManagementTests`, `FinanceManagementTests`, `ExamManagementTests`, `HrmsManagementTests`, `AmsManagementTests`, `OperationsManagementTests`, `LearningManagementTests`, `CollegeManagementTests` and `CrossModuleWorkflowTests` in `tests/EduVerse.Api.Tests`.
- Full student journey: `ExamManagementTests.AdmissionEnrollmentAttendanceFeesExamsResultsJourneyUsesOneStudent` uses public admission, approval, enrollment, attendance, fee collection/replay/receipt, marks, publication and results against MySQL in CI. Local runs without `EDUVERSE_TEST_MYSQL` do not execute database fixtures.
- Activity rendering and named selections: `tests/EduVerse.Portal.Tests/IndividualActivityPageTests.cs` and `tests/EduVerse.Ui.Tests`.
- CI: `.github/workflows/ci.yml` applies migrations 001–021 and requires `EDUVERSE_TEST_MYSQL` before the integration test stage. Merged PRs #78 and #79 passed the complete workflow; subsequent changes must pass their own CI.
- Operational acceptance: `docs/PHASE19_PILOT_READINESS.md`. Roadmap checkboxes do not replace live pilot evidence.
