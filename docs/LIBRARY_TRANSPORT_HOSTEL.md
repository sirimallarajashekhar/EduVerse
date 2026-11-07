# EduVerse Phase 14 — Library, Transport & Hostel

## Architecture
ASP.NET Core 10 modular API-first operations. Portal Razor `/admin/operations` is thin and interacts solely with the authenticated CSRF-protected `/bff/operations/*` BFF. The API holds all business logic, calculations, tenant security and MySQL transactions.

Apply `database/migrations/012_library_transport_hostel.sql` after migrations 001–011, using migration-only credentials. CI applies 001–012 to MySQL 8. Schema readiness requires 012.

Access requires a validated JWT, live ADMIN tenant membership, active institution/plan with `core` entitlement. Campus IDs are validated inside tenant-scoped MySQL operations. All foreign keys for student, books, vehicles, routes, stops, rooms and beds are composite tenant/campus constraints.

## Library
- Title catalog, ISBN and author metadata, uniquely barcoded campus copy records.
- Atomic checkout changes AVAILABLE copy to LENT, after checking active student and reservation priority; cannot lend same copy twice.
- Reservation queue prioritizes first waiting student on a title; fulfilling a loan advances their WAITING reservations to FULFILLED. No public checkout, cancellation, automatic notification or reservation expiry yet.
- Returns compute **₹2 per overdue calendar day, capped at ₹500**, as a recorded fine in INR. Fine collection/payment is not part of Phase 14. Dashboard sums recorded return fines rather than true unpaid balances. The period while a loan is still overdue is tracked separately; fines are posted only at return.
- Read endpoints for loans, reservations and operational overdue summaries.

## Transport
- Campus vehicles with registration, seat capacity and driver contact.
- Routes and ordered stops, linked to same-campus vehicle. Atomic route assignment verifies same-campus active student, route/stop relationship, active-assignment uniqueness, and vehicle capacity **across all routes using that vehicle**.
- Release then assign another route implements transfer with history; direct cross-campus transfer is out of scope.
- No driver identity/background verification, GPS, live vehicle tracking, attendance at stops, route scheduling by timeslot, vehicle maintenance or rider safety alert integrations. Capacity is conservative: shared routes count against one vehicle simultaneously.

## Hostel
- Hostel blocks, rooms and individually numbered beds.
- Atomic student allocation checks active student and available same-campus bed. Database-generated partial uniqueness emulation ensures one active bed per student and one active student per bed.
- Vacate then allocate enables transfers while retaining occupancy history. Report shows active occupancy by campus.
- No gender/age eligibility restrictions, hostel wardens, visitation, curfew, food, fee billing, housekeeping or student self-service in this milestone.

## REST routes
Protected `/api/v1/operations` exposes GET/POST:
- `/titles`, `/copies`, `/loans`, `/reservations`.
- `POST /loans/{id}/return?campusId=...`.
- `/vehicles`, `/routes`, `/stops`, `/route-assignments`.
- `POST /route-assignments/{id}/release?campusId=...`.
- `/hostel-blocks`, `/hostel-rooms`, `/hostel-beds`, `/hostel-allocations`.
- `POST /hostel-allocations/{id}/vacate?campusId=...`.
- `GET /dashboard?campusId=...`: titles, free copies, open/overdue loans, recorded return fines, routes, assigned riders, total and occupied hostel beds.

## Validation and scope
- All state changes are MySQL transactions using a tenant row lock for checkout, returns, reservations, transport assignments, and hostel occupancy to serialize races.
- No overbooking/double circulation; student/stop/bed must belong to the authenticated tenant and selected campus.
- List responses currently cap at 200–1000 records by resource; operational pagination and bulk imports are future enhancements.
- Auth role is ADMIN for this initial version. Role-specific librarian, transport officer and hostel warden permissions are needed before a wider production launch.
- Student data and drivers' phone details are sensitive; enable fine-grained permissions, MFA, audit review, retention and privacy controls prior to production.
- Tests cover checkout/return/reservation priority, vehicle capacity/reassignment, hostel transfer and occupied-bed exclusion, API tenant/role boundaries and protected portal access.
