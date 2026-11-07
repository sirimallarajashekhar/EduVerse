# EduVerse Phase 10 — Fees and Finance

## Security and scope
API-first ASP.NET Core 10, MySQL 8; tenant identity is derived from validated JWT only. Every finance API verifies **live tenant ADMIN membership**, a currently active institution subscription with core entitlement, and tenant/campus ownership. The Razor portal at `/admin/finance` is presentation-only and accesses a CSRF-protected same-origin BFF.

This phase intentionally records **offline/manual payments**, not actual card/UPI checkout or payment gateway settlements. The use of `UPI_MANUAL`, `CARD_MANUAL`, or `BANK_TRANSFER` does **not** verify payment with a provider. Operators must independently confirm funds before recording them.

## Database schema — 008
Apply `database/migrations/008_fees_finance.sql` after migrations 001–007 using a separate migration-only account. No migration runs on API startup.
- `finance_fee_structures`: academic-year/campus fee types, INR decimal(18,2)
- `finance_student_charges`: student enrollment / fee assignment, frozen gross, accumulated concessions
- `finance_installments`: installment dates and exact gross allocations (1–24 per charge)
- `finance_concessions`: recorded scholarship, concession or discount and approver
- `finance_payments`: collected amount, method, receipt, SHA-256 request digest, unique tenant-scoped idempotency key, reconciliation status
- `finance_refunds`: recorded offline refunds with unique tenant-scoped idempotency key and bounded cumulative amount

## REST API (all require authentication and live ADMIN access)
| Method | Endpoint | Description |
|---|---|---|
| GET/POST | `/api/v1/finance/fees` | Year-scoped fee structures |
| GET/POST | `/api/v1/finance/charges` | Assign fees and installment schedules |
| GET | `/api/v1/finance/charges/{id}/installments?campusId=...` | Installment balances |
| POST | `/api/v1/finance/concessions` | Scholarship/concession/discount |
| POST | `/api/v1/finance/payments` | Idempotent manual collection |
| GET | `/api/v1/finance/payments?campusId=...` | Recorded payments |
| GET | `/api/v1/finance/payments/{id}/receipt?campusId=...` | Structured receipt data |
| POST | `/api/v1/finance/refunds` | Idempotent offline refund recording |
| POST | `/api/v1/finance/reconciliation` | Pending → MATCHED/DISPUTED |
| GET | `/api/v1/finance/dashboard?campusId=...` | Financial dashboard |

All POST bodies are typed. An administrator selects their campus, never an arbitrary tenant. API authorization is repeated on retries.

## Accounting invariants
- Currency is **INR**. All money is C# decimal, MySQL DECIMAL(18,2); never binary float in accounting.
- Assignments require an active student enrollment and fee structure from the *same campus, tenant and academic year*. Installments must sum exactly to the fee structure amount.
- Concessions cannot exceed remaining net outstanding. The total amount due is `gross - concessions - payments + recorded refunds`.
- Collections require a visible ASCII `Idempotency-Key` (8–128 chars). Repeated key + identical normalized request returns original payment/receipt. Same key + changed details returns 409. A transaction locks the tenant row, then charge, and prevents overpayment of both the charge and chosen installment.
- Refunds require their own idempotency key; cumulative refunds of one payment cannot exceed that payment's original collected value.
- Receipts are generated deterministically from recorded payment IDs and never returned from another tenant/campus.
- Reconciliation is a manually confirmed transition out of PENDING. There is no provider webhook or bank file matching yet.

## Operational risks and future work
- The dashboard overdue amount is **a conservative estimate**: it caps gross overdue installment amounts at net outstanding. Discounts are not yet allocated to specific installments, so the precise installment due/overdue calculation needs improvement.
- Financial posting/refund audit history beyond the immutable payment and refund rows, privileged approval chains, double-entry general ledger, taxes, fee adjustments, waivers after payment, late fines, payment gateway integration, credit notes, partial settlement allocation, and automated statements are future phases.
- A receipt is transaction evidence **within EduVerse only**, not bank/provider verification.
- One tenant-wide MySQL row lock serializes collections/refunds to prevent duplicate concurrent posting; this favors safety over large-scale write throughput.
- Idempotency keys are retained indefinitely. Define a governed retention/replay horizon before pruning.
- Log minimal financial metadata; do not expose passwords, secrets, payment account details or sensitive student documents.
- Protect finance with least-privilege MySQL credentials, secure backups, HTTPS, MFA for administrators, two-person approval for refunds where required, and formal audit/reconciliation policies before production use.
- The current UI uses JavaScript to split a fee into up to 3 installments with whole-paise math. The API requires an exact decimal sum and is the financial authority.
- The UI's idempotency keys are newly generated for each submission; clients must persist/reuse keys for network retries to get the full benefit of idempotency.

## CI validation
GitHub Actions applies MySQL migrations 001–008, builds and tests .NET 10 solution, runs real JWT/MySQL tests for fee assignment, money rounding, duplicate receipt retries, refunds, reconciliation and tenant security, then verifies API, Portal, AMS and HRMS publishing.
