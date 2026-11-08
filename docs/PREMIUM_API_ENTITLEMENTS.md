# EduVerse finance/college API entitlement enforcement

## Scope

The API now evaluates the active institution membership, institution kind and subscription at request time for **all Finance** (`/api/v1/finance/**`) and **Advanced College** (`/api/v1/college/**`) endpoints, including GET, POST and downloads.

- Missing/expired/suspended subscriptions or missing feature -> HTTP 403 (even if the route is called directly).
- Unknown/inactive tenant membership -> HTTP 403; unauthenticated -> HTTP 401.
- College endpoints additionally require `College` or `Mixed` institution kind. School tenants cannot access college APIs even when their plan contains `college`.
- Existing campus and role checks continue inside domain services. A feature grant does **not** grant an otherwise unauthorized user access.
- No browser-side permission state is trusted; no tenant ID from headers or request body is used for determining entitlements.
- Feature checks use the existing `IInstitutionAdminStore.SubscriptionAsync` logic, which returns no active features for expired or non-active plans.

## Migration / rollout

Apply `017_finance_college_entitlements.sql` after 016. This adds `finance` and `college` to the STANDARD plan. It does **not** modify PILOT tenants or automatically upgrade existing subscriptions. Approved schools provisioned under PILOT will see these modules as unavailable until an authorized operator assigns an appropriate plan.

The plan migration is intentionally independent of automatic onboarding. Review subscription assignments through a secure operator procedure, not ad hoc URL manipulation.

## Follow-up security work

This PR secures the Finance and Advanced College API families. AMS, HRMS, Learning, Operations, Reporting and Workflows require a separate entitlement audit/enforcement pass; do not assume every module has subscription enforcement yet.

CI runs MySQL 8 integration tests covering both allowed access and denied suspended/unentitled access.
