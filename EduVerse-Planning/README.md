# EduVerse ERP

Independent, scalable, multi-tenant ERP for schools and colleges.

## Core architecture
- .NET 10 REST APIs with API-first module boundaries.
- Razor/MVC + Bootstrap + vanilla JavaScript/jQuery; no Angular.
- Every UI module calls only its own API; no business logic or SQL in UI.
- HRMS and AMS independently deployable route-based micro frontends under a shared portal via YARP.
- MySQL 8 with tenant isolation, migrations, indexes and audit trails.
- Centralized identity and authorization; stateless API services and background processing.

## Delivery
Review [the 19-phase roadmap](docs/ROADMAP.md). Each phase requires tested APIs, functional UI where applicable, documented migrations, tenant-isolation checks and acceptance approval.

## Status
Architecture and planning only. Application code is not yet implemented.
