# EduVerse ERP — Phase-wise Roadmap

**Architecture:** .NET 10 REST APIs, MySQL 8, thin ASP.NET Core MVC/Razor + Bootstrap + JavaScript UI, YARP route-based micro frontends for independently deployable modules. No Angular. Every UI module calls its own module API; all business rules, authorization and persistence are backend-owned. Tenant isolation and scalability are architectural requirements.

**Workflow:** main is the default branch. Use feature branches, PR reviews, CI checks, migration reviews and acceptance tests. Status checkboxes below start unchecked until verified.

## Phase 01 — Project Setup & GitHub

- [ ] **P01-T01** Create EduVerse.slnx and source structure
- [ ] **P01-T02** Define main and feature branch workflow
- [ ] **P01-T03** Add coding conventions and repository documentation
- [ ] **P01-T04** Configure GitHub Actions build checks
- [ ] **P01-T05** Create baseline testing projects

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 02 — Architecture & Portal Shell

- [ ] **P02-T01** Define bounded module ownership and REST contracts
- [ ] **P02-T02** Build MVC/Razor Portal Shell and Bootstrap design system
- [ ] **P02-T03** Configure YARP route-based micro frontends
- [ ] **P02-T04** Implement module navigation and feature discovery
- [ ] **P02-T05** Specify module deployment/version contracts

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 03 — Multi-Tenant Database

- [ ] **P03-T01** Design institutions, campuses and tenant data model
- [ ] **P03-T02** Create MySQL migrations, indexes and constraints
- [ ] **P03-T03** Implement trusted tenant resolution
- [ ] **P03-T04** Enforce tenant isolation in queries and services
- [ ] **P03-T05** Automate cross-tenant security tests

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 04 — Identity & Permissions

- [ ] **P04-T01** Configure centralized authentication and SSO
- [ ] **P04-T02** Implement roles, claims and permission policies
- [ ] **P04-T03** Build login, logout and account recovery
- [ ] **P04-T04** Add session controls and audit trail
- [ ] **P04-T05** Test authorization by tenant and role

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 05 — API Standards & Scalability

- [ ] **P05-T01** Standardize API versioning, validation and errors
- [ ] **P05-T02** Implement pagination, sorting and filtering
- [ ] **P05-T03** Configure health checks and observability
- [ ] **P05-T04** Introduce idempotency and background work conventions
- [ ] **P05-T05** Create load and horizontal-scaling tests

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 06 — Institution Administration

- [ ] **P06-T01** Build institution registration and onboarding
- [ ] **P06-T02** Configure schools, colleges and campuses
- [ ] **P06-T03** Manage departments, buildings and institution settings
- [ ] **P06-T04** Implement subscriptions and feature access
- [ ] **P06-T05** Create institution administration screens

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 07 — Academic Management

- [ ] **P07-T01** Manage academic years, classes and sections
- [ ] **P07-T02** Configure departments, programs and semesters
- [ ] **P07-T03** Manage subjects, credits and curricula
- [ ] **P07-T04** Implement teacher/faculty assignments
- [ ] **P07-T05** Build academic calendar and timetable APIs

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 08 — Admissions & Students

- [ ] **P08-T01** Implement enquiries and admission applications
- [ ] **P08-T02** Configure admission approvals and enrollment
- [ ] **P08-T03** Manage student and guardian profiles
- [ ] **P08-T04** Implement promotions, transfers and withdrawals
- [ ] **P08-T05** Generate student ID and academic documents

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 09 — Attendance & Timetables

- [ ] **P09-T01** Build student attendance workflows
- [ ] **P09-T02** Implement subject-wise college attendance
- [ ] **P09-T03** Manage periods, substitutions and schedules
- [ ] **P09-T04** Implement corrections and approvals
- [ ] **P09-T05** Generate attendance and shortage reports

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 10 — Fees & Finance

- [ ] **P10-T01** Configure fees, installments and concessions
- [ ] **P10-T02** Implement invoices and student balances
- [ ] **P10-T03** Implement payments, receipts and refunds
- [ ] **P10-T04** Build reconciliation and approval workflows
- [ ] **P10-T05** Create financial reports and audit logs

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 11 — Exams & Results

- [ ] **P11-T01** Configure examination types and schedules
- [ ] **P11-T02** Build marks entry and moderation workflows
- [ ] **P11-T03** Calculate school grades and report cards
- [ ] **P11-T04** Implement semester grades, SGPA and CGPA
- [ ] **P11-T05** Publish results, transcripts and certificates

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 12 — HRMS Micro Frontend

- [ ] **P12-T01** Build independent HRMS Razor frontend and API
- [ ] **P12-T02** Implement employee, faculty and recruitment data
- [ ] **P12-T03** Build staff attendance, shifts and leave
- [ ] **P12-T04** Implement payroll, payslips and statutory settings
- [ ] **P12-T05** Add appraisal, employee self-service and exits

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 13 — AMS Micro Frontend

- [ ] **P13-T01** Build independent AMS Razor frontend and API
- [ ] **P13-T02** Implement procurement, categories and asset register
- [ ] **P13-T03** Manage allocation, transfers and returns
- [ ] **P13-T04** Implement maintenance, warranty and QR verification
- [ ] **P13-T05** Build inventory, depreciation and disposal reports

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 14 — Library Transport Hostel

- [ ] **P14-T01** Implement library catalog and circulation
- [ ] **P14-T02** Build loans, returns and fine handling
- [ ] **P14-T03** Manage transport vehicles, routes and allocations
- [ ] **P14-T04** Manage hostel rooms, beds and allocations
- [ ] **P14-T05** Integrate fees and operations reports

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 15 — Learning & Communication

- [ ] **P15-T01** Implement homework, materials and submissions
- [ ] **P15-T02** Build student and parent portal interfaces
- [ ] **P15-T03** Manage announcements and circulars
- [ ] **P15-T04** Integrate SMS, email and WhatsApp providers
- [ ] **P15-T05** Track templates, notifications and delivery history

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 16 — Advanced College

- [ ] **P16-T01** Build course registration and electives
- [ ] **P16-T02** Enforce prerequisites and credit requirements
- [ ] **P16-T03** Manage internships and placements
- [ ] **P16-T04** Track research, publications and alumni
- [ ] **P16-T05** Implement graduation and accreditation reporting

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 17 — Cross-Module Workflows

- [ ] **P17-T01** Connect HRMS onboarding with Identity
- [ ] **P17-T02** Connect HRMS offboarding with AMS clearance
- [ ] **P17-T03** Implement finance/procurement integration contracts
- [ ] **P17-T04** Introduce reliable messaging and outbox processing
- [ ] **P17-T05** Test school and college cross-module workflows

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 18 — Reporting Security Quality

- [ ] **P18-T01** Build administrative analytics dashboards
- [ ] **P18-T02** Implement PDF, Excel and scheduled reports
- [ ] **P18-T03** Automate unit, integration and UI tests
- [ ] **P18-T04** Run tenant isolation and accessibility assessments
- [ ] **P18-T05** Run performance, security and resilience tests

**Done when:** API contract and security review passed, code built and tested, documentation updated, acceptance evidence recorded.

## Phase 19 — Deployment & Pilot

- [ ] **P19-T01** Implement module-specific CI/CD pipelines
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
