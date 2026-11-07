# EduVerse ERP — Phase-wise Development Tasks

**Engineering rules:** ASP.NET Core 10 API-first; ASP.NET Core MVC/Razor, Bootstrap, JS/jQuery UI; no Angular. UI calls only the corresponding module API, never the DB. Business rules, calculations, permissions and persistence belong to the API/application layer. Tenant isolation and secure audit logs are required throughout. HRMS and AMS have independently deployable route-based micro frontends.

**Task status:** All unchecked. These are planned tasks, not completed features.

## Phase 01 — Project Setup & GitHub

- [ ] P01-T01: Create main branch workflow, protection policy and PR template
- [ ] P01-T02: Create EduVerse.slnx with source/test folder conventions
- [ ] P01-T03: Add coding standards, README, architecture decision records
- [ ] P01-T04: Configure CI build, tests and dependency security checks
- [ ] P01-T05: Set up local development configuration without committing secrets

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 02 — Portal Shell & Micro Frontend Architecture

- [ ] P02-T01: Define independently deployable module boundaries and API ownership
- [ ] P02-T02: Build Razor portal shell, layout and shared UI design package
- [ ] P02-T03: Configure YARP routing for /hrms, /ams and other module routes
- [ ] P02-T04: Specify shared login, tenant context and navigation contract
- [ ] P02-T05: Document frontend/API versioning and deployment compatibility

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 03 — Multi-Tenancy & Data Platform

- [ ] P03-T01: Model institutions, campuses and academic tenants
- [ ] P03-T02: Design MySQL schemas, constraints, indexes and migration conventions
- [ ] P03-T03: Implement tenant context from authenticated claims/session
- [ ] P03-T04: Enforce tenant scope in repositories and service queries
- [ ] P03-T05: Test cross-tenant access, imports and reporting isolation

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 04 — Identity, Security & RBAC

- [ ] P04-T01: Set up central authentication and SSO across modules
- [ ] P04-T02: Implement tenant and campus-aware role/permission model
- [ ] P04-T03: Build user management, invites and account recovery
- [ ] P04-T04: Configure secure auth cookies, CSRF protection and API authorization
- [ ] P04-T05: Add security audit events, rate limits and access tests

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 05 — Shared API & Scalability Foundation

- [ ] P05-T01: Define REST contracts, versioning, validation/error formats
- [ ] P05-T02: Add pagination, filtering, sorting and concurrency tokens
- [ ] P05-T03: Add health checks, structured telemetry and tracing
- [ ] P05-T04: Create job/outbox and object-storage interfaces
- [ ] P05-T05: Run baseline performance, resilience and horizontal-scaling tests

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 06 — Institution Administration

- [ ] P06-T01: Implement institution onboarding and verification
- [ ] P06-T02: Manage campuses, buildings, locations and institution profiles
- [ ] P06-T03: Configure school/college types and feature entitlements
- [ ] P06-T04: Manage annual settings, holidays and preferences
- [ ] P06-T05: Build management dashboard and institution APIs

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 07 — Academics & Curriculum

- [ ] P07-T01: Set up classes, sections, departments and programs
- [ ] P07-T02: Maintain subjects, semesters, credits and curriculum
- [ ] P07-T03: Configure academic years/terms and grading scales
- [ ] P07-T04: Build timetables and faculty/subject allocations via APIs
- [ ] P07-T05: Add validation and academic configuration UI

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 08 — Admissions & Student Lifecycle

- [ ] P08-T01: Implement admission enquiry, application and review
- [ ] P08-T02: Approve enrollment and allocate unique admission identifiers
- [ ] P08-T03: Store student/guardian details and secured documents
- [ ] P08-T04: Implement promotions, transfers, withdrawals and certificates
- [ ] P08-T05: Test admissions data and tenant isolation

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 09 — Student Attendance & Timetable Operations

- [ ] P09-T01: Build daily and subject-wise attendance APIs
- [ ] P09-T02: Manage attendance policies and exception approvals
- [ ] P09-T03: Support timetable substitutions and schedule changes
- [ ] P09-T04: Generate absentee alerts and shortage reports
- [ ] P09-T05: Add teacher portal attendance screens and tests

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 10 — Student Fees & Finance

- [ ] P10-T01: Define fee schedules, installments, discounts and concessions
- [ ] P10-T02: Generate invoices, dues, receipts, refunds and adjustments
- [ ] P10-T03: Integrate payment processor with server-side verification
- [ ] P10-T04: Reconcile transactions and approve exceptional changes
- [ ] P10-T05: Implement financial reports, access controls and audit testing

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 11 — Examinations & Results

- [ ] P11-T01: Create examination schedules and marks entry
- [ ] P11-T02: Apply assessment rules/grade calculations in API services
- [ ] P11-T03: Generate school report cards and promotion decisions
- [ ] P11-T04: Compute college SGPA/CGPA and transcripts
- [ ] P11-T05: Support publication, corrections and result access rules

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 12 — HRMS Micro Frontend

- [ ] P12-T01: Build independently deployable HRMS Razor app and API
- [ ] P12-T02: Manage employees, recruitment, onboarding and organization hierarchy
- [ ] P12-T03: Build staff attendance, shifts, leave and approvals
- [ ] P12-T04: Implement payroll, payslips and configurable statutory rules
- [ ] P12-T05: Add faculty integration, appraisals, self-service, exit clearance

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 13 — AMS Micro Frontend

- [ ] P13-T01: Build independently deployable AMS Razor app and API
- [ ] P13-T02: Manage categories, purchases, vendors and asset registry
- [ ] P13-T03: Implement asset allocation, transfers, returns and QR labels
- [ ] P13-T04: Handle maintenance, warranties, inventory and depreciation
- [ ] P13-T05: Support physical verification, disposals, auditing and reports

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 14 — Library, Transport & Hostel

- [ ] P14-T01: Implement catalog, borrowing, renewals and library fines
- [ ] P14-T02: Manage vehicles, routes, drivers and student allocations
- [ ] P14-T03: Implement hostel rooms, beds and residential assignments
- [ ] P14-T04: Integrate payments/fees via module contracts
- [ ] P14-T05: Deliver operational dashboards, exports and tests

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 15 — Learning, Parent Portal & Communication

- [ ] P15-T01: Create homework, assignments and resource APIs
- [ ] P15-T02: Build teacher, student and guardian portal screens
- [ ] P15-T03: Implement notifications and institutional announcements
- [ ] P15-T04: Integrate SMS, WhatsApp and email with consent/templates
- [ ] P15-T05: Monitor deliveries, failures and retry processing

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 16 — Advanced College Services

- [ ] P16-T01: Enable course registration, prerequisites and electives
- [ ] P16-T02: Implement credit progression and academic eligibility
- [ ] P16-T03: Manage internship and placement workflows
- [ ] P16-T04: Build research, publications and alumni workflows
- [ ] P16-T05: Generate accreditation-supporting records and reports

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 17 — Cross-Module Orchestration

- [ ] P17-T01: Connect employee onboarding to centralized Identity
- [ ] P17-T02: Connect employee clearance to AMS and other modules
- [ ] P17-T03: Define contracts between procurement, AMS and Finance
- [ ] P17-T04: Implement reliable events, outbox and idempotent handlers
- [ ] P17-T05: Test multi-module workflows, retries and failure recovery

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 18 — Reports, Testing & Production Hardening

- [ ] P18-T01: Create centralized role-based reporting and exports
- [ ] P18-T02: Complete unit, API, integration and browser E2E tests
- [ ] P18-T03: Review security, PII access, accessibility and tenant isolation
- [ ] P18-T04: Load test representative schools/colleges and size indexes
- [ ] P18-T05: Validate backup, restoration and incident recovery

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

## Phase 19 — Deployment, Pilot & Release

- [ ] P19-T01: Create CI/CD for portal, APIs, HRMS and AMS separately
- [ ] P19-T02: Deploy staging environments, secrets and MySQL migrations
- [ ] P19-T03: Pilot with a school and a college using synthetic/sanitized data
- [ ] P19-T04: Collect UAT signoff and finalize manuals/training
- [ ] P19-T05: Release production with monitoring, support and rollback procedures

**Exit gate:** APIs and UI (where applicable) tested; tenant security checked; docs and migrations included; reviewed PR merged.

