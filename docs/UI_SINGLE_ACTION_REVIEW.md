# Individual activity pages

Module URLs now open an activity menu. Each menu link opens a separate MVC GET route under `<module>/activities/<activity>`. The server checks a compile-time activity allowlist and removes unrelated activity markup before producing the response. Activity display no longer depends on JavaScript or CSS hiding panels.

Examples:

| Activity | URL |
| --- | --- |
| Finance menu | `/admin/finance` |
| Create fee | `/admin/finance/activities/fee-form` |
| Record payment | `/admin/finance/activities/payment-form` |
| Refund | `/admin/finance/activities/refund-form` |
| Reconciliation | `/admin/finance/activities/reconcile-form` |
| Library book title | `/admin/operations/activities/title-form` |
| Library copy | `/admin/operations/activities/copy-form` |
| Approve registration | `/platform/registrations/{id}/approve` |
| Reject registration | `/platform/registrations/{id}/reject` |

The shared ActivityOnlyTagHelper suppresses unrequested branches, including their forms and surrounding grid columns. Shared campus/record selectors are rendered only where needed; forms and inputs preserve their existing IDs and API contracts. Unknown activity names return 404. All module/controller authorization remains in force. No billing, business logic, database access or API authorization was moved to the UI.

The module API scripts now tolerate absent controls rather than expecting the entire module page. Native form association is retained for shared fields. Parent/student record selection and platform institution selection remain available on their dependent activity pages.

Validation:

- `npm ci --prefix tests/EduVerse.Ui.Tests` and `npm test --prefix tests/EduVerse.Ui.Tests` check activity projection with no hiding script and initialize the real UI adapters with all other activities absent.
- `IndividualActivityPageTests` uses the actual MVC/Razor pipeline to verify menus, allowlisted activity URLs, unknown URLs, and the absence of unrelated forms in returned HTML. It does not execute JavaScript.
- Registration decisions have dedicated server GET pages with one POST form each.
- Existing authenticated page tests were updated to follow menu links to individual activities.

The previous PILOT entitlement migration remains unchanged. No additional database migration is required for this UI change. A full build and the MVC rendering tests must pass CI before merge; live API/database and responsive visual verification are separate from the DOM fixture checks.

## Specific operations

Menus split combined-operation forms into dedicated URLs. Create Class, Create Subject, Create Department, Create Building, Receive Stock, Issue Stock, Adjust Stock, Promote Student, Transfer Student, Withdraw Student and each employee status transition now have their own activity. The operation is rendered as fixed text plus the existing API field; no action-switching dropdown appears on those pages. Old combined-operation URLs redirect to their module menu.

Promotion has its own required destination academic year and grade/class/section fields. It no longer reads inputs from the unrelated enrollment form. Transfer renders a destination campus, while withdrawal renders no destination fields. API validation and authorization remain authoritative for all submitted values.
