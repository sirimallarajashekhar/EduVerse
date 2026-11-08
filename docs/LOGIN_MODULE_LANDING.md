# EduVerse login → modules route

## Login URLs and destinations

| Account | Sign-in URL | Successful destination |
| --- | --- | --- |
| Platform Super Admin | `/platform/login` | `/platform/console` |
| Institution Admin | `/account/login` (institution code required) | `/modules` |
| Institution Member | `/account/login` (institution code required) | `/modules` → assigned teacher/parent/student portals |

The public home `/` now redirects authenticated users to the same role-specific destination. Both login forms redirect a user who is already authenticated to their current workspace rather than displaying a second login.

`/platform/console` automatically requests `/bff/platform-control/modules` and displays all module definitions from the API-backed tenant catalog plus independently deployed AMS/HRMS. It *does not* enable tenant-scoped writes; select a registered institution to review its actual subscription and permitted modules.

`/modules` for an institution Admin uses `/bff/tenant/modules`, with server-side permission and subscription decisions. No module list is hardcoded in JavaScript.

## Local verification

1. Pull the merged changes, rebuild and restart **EduVerse.Api** on `https://localhost:7201` and **EduVerse.Web** on `https://localhost:61274`. Both are needed; avoid mixing old and new builds.
2. Verify `https://localhost:7201/api/v1/system/info` responds. Check `Api:BaseUrl` in Web's active environment.
3. Open an incognito browser window, avoiding an old `.EduVerse.Auth` cookie, and visit `https://localhost:61274/platform/login`. Use a **platform operator** account, not an institution admin account.
4. After sign-in confirm `/platform/console` displays the *All EduVerse modules* cards without selecting an institution. `/bff/platform-control/modules` should return 200 with a valid platform cookie.
5. Sign out and test `/account/login` with a valid institution code and an **institution** account. Confirm `/modules` and enabled module cards. `/bff/tenant/modules` should return 200.
6. If sign-in appears successful but the browser returns to login, inspect Developer Tools → Network for `/api/v1/platform-admin/session` (called server-side by Web) and Web application logs. A rejected cookie/session or mismatched `Api:BaseUrl` must be fixed; do not bypass session validation.

**No new MySQL migration is needed.** Run the module catalog/UI and cookie integration tests in CI.
