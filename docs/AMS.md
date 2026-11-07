# EduVerse Phase 13 — Asset & Inventory Management System

## Architecture
AMS remains an **independently deployable** ASP.NET Core 10 Razor/MVC micro frontend at `/ams`, mounted by the EduVerse Portal through YARP. A same-origin CSRF-protected authenticated BFF at `/ams/bff/*` forwards the user's bearer token. **All domain rules, authorization and MySQL access are API-owned**, implemented in `EduVerse.Api`, `EduVerse.Application.Ams` and `EduVerse.Infrastructure.MySql`.

All AMS endpoints require a currently active tenant ADMIN, a valid live API session, and the existing **STANDARD-plan `ams` entitlement**. PILOT does not grant AMS. The tenant ID comes exclusively from validated JWT claims; campus records and references are scoped to that tenant.

## Database migration 011
Apply `database/migrations/011_ams_assets_inventory.sql` after migrations 001–010 with a dedicated migration account. API readiness checks require version 011.
- `ams_categories`: campus categories and codes.
- `ams_assets`: unique tenant-level generated EVA asset code; campus category, optional building/department/HR employee, acquisition cost and lifecycle.
- `ams_asset_events`: append-only actor-attributed registration, allocation, transfer, return, repair and disposal history.
- `ams_maintenance`: scheduled work order with open/completed status.
- `ams_stock_items`: campus SKU, units, quantity and reorder threshold.
- `ams_stock_movements`: immutable movement ledger with unique tenant idempotency key, SHA-256 payload digest, actor, and resulting balance.
- `ams_vendors`: campus vendors.
- `ams_purchase_orders`: draft purchase order, quantity, unit price and total.

All dependent asset/stock/PO foreign keys enforce tenant+campus scope, including building, department and employee references.

## REST API
All paths are relative to `/api/v1/ams`. GET endpoints use `campusId`; the write DTOs carry campus IDs and never tenant IDs.

| Method | Path | Usage |
|---|---|---|
| GET/POST | `/categories` | Manage asset categories |
| GET/POST | `/vendors` | Vendor registry |
| GET/POST | `/assets` | Register and list fixed assets |
| POST | `/assets/allocate` | Assign or transfer within campus to building/department/employee |
| POST | `/assets/lifecycle` | Return, maintenance, repair, retirement and disposal |
| GET | `/assets/{id}/events` | Lifecycle audit |
| GET/POST | `/maintenance` | Work orders and schedules |
| POST | `/maintenance/{id}/complete?campusId=...` | Complete work order |
| GET/POST | `/stock` | Inventory stock and low-stock flag |
| POST | `/stock/movements` | Idempotent IN, OUT and positive ADJUST |
| GET/POST | `/purchases` | Draft purchase orders |
| GET | `/dashboard` | Campus metrics |

## Core invariants
- Asset state transitions are enforced by the backend; an `ALLOCATED` asset can be returned, then retired and disposed. Maintenance and repair transitions have explicit guards. Disposed/retired assets cannot be reallocated.
- Allocation/transfer targets must exist, be active and belong to the same tenant/campus. Cross-campus asset transfer is **not** supported in this milestone, because it requires an atomic two-campus custody ledger.
- Stock movement requires `Idempotency-Key` of 8–128 printable ASCII characters. Repeating the same key and normalized request returns the same original movement; changing the payload with the same key yields HTTP 409. Tenant-row InnoDB locks serialize stock operations and protect against concurrent negative quantities.
- `OUT` subtracts quantity. `IN` and initial `ADJUST` are positive additions; negative stock adjustments, shrinkage approvals, stocktakes and controlled variance journaling are future work.
- `quantity <= reorder_level` flags low stock. No external alerts or notifications yet.
- Purchase orders are **DRAFT records** only; they do not reserve or receive stock, pay vendors, approve spending or update inventory. Receipt of goods requires a separate verified `IN` stock movement.
- Monetary purchase amounts and stock quantities use MySQL DECIMAL(18,2) and C# decimal rather than floating point.
- The API currently limits AMS operations to the tenant ADMIN role; more granular custodians, warehouse clerks and approvals belong in the future RBAC expansion.

## Production considerations
The scope is an AMS administrative foundation. Future enhancements include purchase approvals, GRNs, supplier invoices, warranty reminders, preventive recurring schedules, inventory adjustment approvals, stock reservation, barcode/QR scanning, depreciation, asset valuations and decommission authorizations. Protect financial/inventory audit records against direct modifications, use least-privilege credentials and perform backup/restore and access-review exercises before production rollout.

## CI
GitHub Actions starts MySQL 8, migrates 001–011, builds/tests the .NET 10 solution and publishes API, Portal, AMS and HRMS separately. Integration tests exercise real MySQL/JWT asset lifecycle, work orders, draft purchasing, stock replay/concurrency and tenant scope. AMS frontend tests reject anonymous access.
