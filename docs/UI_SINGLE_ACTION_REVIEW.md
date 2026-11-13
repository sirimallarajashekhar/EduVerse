# Single-action screen review

Each workspace uses explicit action declarations. A dropdown selects a single action address (`#refund-form`, `#reconcile-form`, etc.). Creation forms, record-review activities and standalone commands have independent selections. Fields and API event handlers remain mounted; the UI does not implement business or subscription rules.

## Review coverage

Navigation hubs (home and module landing), login, password-reset, public admission, institution registration and error/access-denied pages were reviewed as separate-purpose pages and retain their current workflows. The following operational templates now declare focused activities. Counts are template states; a rendered approval queue has individual actions for each institution.

| Workspace template | Action selections |
| --- | ---: |
| `src/EduVerse.Ams.Web/Views/Module/Index.cshtml` | 19 |
| `src/EduVerse.Hrms.Web/Views/Module/Index.cshtml` | 12 |
| `src/EduVerse.Web/Views/Academic/Index.cshtml` | 7 |
| `src/EduVerse.Web/Views/AttendanceAdmin/Index.cshtml` | 9 |
| `src/EduVerse.Web/Views/CollegeAdmin/Index.cshtml` | 12 |
| `src/EduVerse.Web/Views/ExamAdmin/Index.cshtml` | 10 |
| `src/EduVerse.Web/Views/FinanceAdmin/Index.cshtml` | 8 |
| `src/EduVerse.Web/Views/InstitutionAdmin/Index.cshtml` | 12 |
| `src/EduVerse.Web/Views/LearningAdmin/Index.cshtml` | 12 |
| `src/EduVerse.Web/Views/OperationsAdmin/Index.cshtml` | 17 |
| `src/EduVerse.Web/Views/PlatformAdmin/Registrations.cshtml` | 4 |
| `src/EduVerse.Web/Views/PlatformConsole/Index.cshtml` | 7 |
| `src/EduVerse.Web/Views/RbacAdmin/Index.cshtml` | 4 |
| `src/EduVerse.Web/Views/Reporting/Index.cshtml` | 13 |
| `src/EduVerse.Web/Views/ReportingAdmin/Index.cshtml` | 1 |
| `src/EduVerse.Web/Views/RolePortal/LearningReview.cshtml` | 1 |
| `src/EduVerse.Web/Views/RolePortal/Parent.cshtml` | 7 |
| `src/EduVerse.Web/Views/RolePortal/Student.cshtml` | 8 |
| `src/EduVerse.Web/Views/RolePortal/Teacher.cshtml` | 12 |
| `src/EduVerse.Web/Views/StudentAdmin/Index.cshtml` | 11 |
| `src/EduVerse.Web/Views/UserAdmin/Index.cshtml` | 2 |
| `src/EduVerse.Web/Views/WorkflowAdmin/Index.cshtml` | 4 |

## Pilot subscription availability

Apply `database/migrations/021_pilot_all_module_access.sql` once to the application database, after migrations 001–020. It is idempotent and grants all ten feature codes to the PILOT plan. Existing active PILOT institutions inherit the expanded features; there is no need to edit each subscription. It also supplies the previously missing STANDARD operations, learning and workflows features.

The API catalog now includes AMS and HRMS, and the sidebar avoids duplicate module links. A Mixed or College institution has all 14 modules; a School has 13 because Advanced College is institution-specific. Test college workflows with a College or Mixed institution. The registration form already supports Mixed (School & College).

Suspended, cancelled, expired or unassigned subscriptions continue to have no active feature set. Institution membership, role permissions, campus scope and record ownership are still validated by the existing APIs. Platform Super Admin remains a platform role; exercise tenant workflows while signed in as an institution administrator.

This change updates source and migration files. It does not apply the migration to a running database.

## Validation

- `npm ci --prefix tests/EduVerse.Ui.Tests` then `npm test --prefix tests/EduVerse.Ui.Tests` runs action-isolation regression tests against real view markup using jsdom.
- Checks cover every declared selection, a maximum of one editable form, duplicate IDs, declarations contained by the workspace, preserved input values and handlers, shared selectors, malformed hashes and legacy/sidebar anchors.
- UI regression and migration 021 are included in GitHub Actions. API catalog and pilot subscription integration expectations were updated.
- Local JavaScript syntax and whitespace checks pass.
- The local environment has no .NET SDK or MySQL client/server. Full Razor compilation, API integration, and migration execution must pass CI or run in the developer environment. jsdom verifies DOM behavior; responsive visual layout still needs browser verification.

## Design

Royal Blue remains the primary button/navigation color. Green marks finance/reporting, purple marks students/HR, sky blue marks academics/attendance, amber marks operations/assets, indigo marks examinations/college, rose marks learning and teal marks administration. Action titles and card borders use the same module accent; labels remain visible so color is never the only cue.
