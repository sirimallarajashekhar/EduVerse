# Tenant-scoped EduVerse school and college navigation

After platform approval (migration 016), sign in as the institution administrator at `/account/login` using the approved institution slug and account credentials.

The dashboard now calls the same-origin BFF endpoint `GET /bff/tenant/modules`. This forwards the authenticated bearer token to `GET /api/v1/tenant/modules`. The API revalidates tenant membership, confirms the administrator role using MySQL, reads the institution type and current subscription, and returns an allowlisted menu.

The public `GET /api/v1/platform/modules` remains a deployment registry for AMS/HRMS micro-frontends; it is **not** the institution navigation catalog. The tenant catalog lists real Razor administration screens and is filtered by institution type (college-only features do not show for a school). Unentitled features appear as disabled, marked 'Subscription required', rather than clickable links.

Initial PILOT includes `core` and `academics`. Other features require appropriate subscription-plan feature assignments. No new migration is needed for this PR. Do not grant a feature by changing browser-side JS. Entitlement requirements for the underlying REST endpoints must also be enforced server-side as a separate security release; this implementation does not claim that hiding links is access control.

## Verification
- API and Web use the same MySQL database and compatible JWT signing configuration.
- API `GET /api/v1/tenant/modules`: anonymous -> 401, ordinary member -> 403, tenant admin -> 200 with filtered modules.
- Web `/bff/tenant/modules` requires authentication and authenticated ADMIN role.
- School ADMIN sees academics and student management; advanced college entry is excluded.
- Platform operator sees the institution approval link, not institution-admin menus.
- Run `dotnet test EduVerse.slnx`, including MySQL integration tests, before merging.
