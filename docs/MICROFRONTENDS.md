# EduVerse Micro Frontend Contract (Phase 2)

## Purpose
Route-based composition with ASP.NET Core MVC/Razor and YARP. No Angular, no iframe, no browser-side federation. Portal and each module deploy independently. Business logic is API-owned and UI is presentation-only.

## Endpoints (development)
| App | Direct URL | Public URL |
|---|---|---|
| Platform REST API | http://localhost:5201/api/v1/ | /bff/platform/modules for registry |
| Portal + YARP | http://localhost:5200/ | / |
| AMS Web | http://localhost:5202/ams | /ams |
| HRMS Web | http://localhost:5203/hrms | /hrms |

Proxy preserves the /ams or /hrms prefix. Modules therefore mount controller routes and any module-specific static assets under their prefix. The shared Razor Class Library (RCL) publishes CSS and JS at /_content/EduVerse.DesignSystem/... which the Portal serves at the same-origin domain. Each module also references the RCL for direct local previews.

## Contracts
The Platform REST API returns a static, allowlisted set of ModuleRegistration objects at GET /api/v1/platform/modules. The Portal BFF simply proxies GET /bff/platform/modules. The shared navigation script renders the module links via safe DOM methods. Module URLs are NOT dynamically inserted into YARP configuration by API responses. ReverseProxy routes and destination addresses are operator-managed app configuration.

## Separation rules
- AMS Web -> AMS REST API only (when implemented); HRMS Web -> HRMS REST API only. Platform navigation is a shared shell concern.
- No direct database or business logic in module MVC controllers, Razor files, or JavaScript.
- Cross-module operations are coordinated by backend application contracts, never browser orchestration.
- Platform API module registry is public *discovery metadata*, not authorization. Phase 4 implements centralized authentication and tenant-aware permissions. Module preview routes are currently public and contain no sensitive data.
- Production reverse proxy targets must be internal/private endpoints. Validate Host/forwarded headers at the perimeter and require HTTPS, SSO, authorization and robust CSRF protection for mutations before production use.

## Local run steps
Start these four processes in separate terminals from the repository root:

```powershell
dotnet run --project src/EduVerse.Api --launch-profile EduVerse.Api
dotnet run --project src/EduVerse.Ams.Web --launch-profile EduVerse.Ams.Web
dotnet run --project src/EduVerse.Hrms.Web --launch-profile EduVerse.Hrms.Web
dotnet run --project src/EduVerse.Web --launch-profile EduVerse.Web
```

Open http://localhost:5200/. The portal navigation should show AMS and HRMS from the API registry. Routes /ams and /hrms should be reverse-proxied to their standalone Razor hosts. Environment overrides use ASP.NET Core configuration, e.g. ReverseProxy__Clusters__ams__Destinations__primary__Address.

## Phase 2 acceptance / future work
- Standalone AMS/HRMS are **preview shells only**, not asset or HR business systems.
- Auth, tenant context, payments, data services, API gateway security and persistence remain in later phases.
- Phase 2 tests exercise the apps independently. Full running-proxy end-to-end integration should be added before a production deployment.
