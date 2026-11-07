# Phase 18 — Reporting, Security & Quality

## Implemented first slice (P18-T01, part of P18-T02/T03/T04)
- `GET /api/v1/reporting/dashboard?campusId=<uuid>&from=YYYY-MM-DD&to=YYYY-MM-DD`: active student/enrollment/staff, asset, pending admission counts; cumulative charge/concession/payment/refund and receivable values; monthly new-enrollment and payment trends.
- `GET /api/v1/reporting/monthly.csv` with the same query: bounded and schema-fixed UTF-8 CSV of month, enrollments, payment counts and collected INR. No student names, mobile numbers, free-form text or other user-authored CSV cells.
- MVC Razor admin page: `/admin/reporting`, same-origin allowlisted `/bff/reporting` proxy, and simple accessible text/table layouts. No UI calculations or database calls.
- Data access requires a valid JWT, live ADMIN membership, active reporting subscription and an active tenant-owned campus. The tenant ID comes only from validated JWT context, never the URL/query string. The HTML proxy additionally checks ADMIN cookie. No anonymous exports.
- Reports use a repeatable-read InnoDB transaction for a consistent multi-table snapshot. Dates are UTC, inclusive endpoints, max span 366 days, and values from parameterized tenant/campus queries. Financial KPI values are cumulative **through** the selected end date; current status counters are current-state (not historic as-of status). Monthly rows are **within** the selected from/to range.
- Built-in test coverage: API integration tests for school and college tenants, privileged and unprivileged members, foreign-campus isolation, report entitlement, invalid dates and PII-free export; Razor authorization tests for page and exports.

## Quality and safety considerations
- Existing finance payments are explicitly manual/offline records. Exporting summaries does not validate external settlement.
- `outstandingInr` = charges - concessions - payments + refunds, aggregating transactions through the chosen end date. It may be negative if refunds/payments are recorded for charges outside the selected period; the API never clamps or conceals discrepancies.
- CSV header is fixed, report data are numeric-only and server-calculated. Browser output uses `textContent`, not HTML injection.
- Do not mistake the new API for a historic student census or a reconciled financial ledger.
- Add appropriate database indexes based on measured `EXPLAIN ANALYZE` plans once realistic aggregate volumes are available.

## Still outstanding before completing Phase 18
- True PDF and XLSX report generation, scheduled reports with outbox/delivery.
- Automated accessibility review (keyboard, screen-reader, axe), UI functional exercises and remediation.
- Load/performance tests, fault injection, observability alerts and security assessment of the whole platform.
- Full end-to-end report acceptance with larger synthetic datasets and documented targets.
- CI green for the final PR commit and security review. Keep this PR in draft until verified.

## Validation
GitHub Actions restores/builds/tests .NET 10 on MySQL 8. This slice does not need a new migration. Reuse schema 001–015. See the feature-branch PR for CI status and review notes.
