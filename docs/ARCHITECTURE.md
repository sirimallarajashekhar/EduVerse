# Phase 1 Architecture and Development Rules

- Independently deployable MVC/Razor micro frontends, ASP.NET Core 10 REST APIs and shared versioned contracts.
- Razor controllers serve views or thin reverse proxies only; no database access or business logic.
- Each UI module calls only its own REST API. Business rules, authorization and persistence belong to backend services.
- Adopt route-based micro frontends through YARP in Phase 2. AMS and HRMS follow the shared shell and SSO patterns.
- Tenant context must be derived from trusted authentication, not caller-provided TenantId. Enforce isolation via repositories and tests.
- Stateless API instances, health endpoints, metrics, structured logging and performance testing.
- Phase 1 implements only an API status endpoint, Razor shell, shared contract, tests and CI; MySQL, tenants, auth and functional modules follow in later phases.
