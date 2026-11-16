# Named selections and student journey

Manual record-reference text fields in teacher, student, parent, learning review, cross-module workflow and HR account-linking activities are now selections of API-authorized records. IDs remain opaque option values; users see names, admission numbers, class/year/subject labels and account emails.

The read-only `/api/v1/activity-lookups/{resource}` endpoint has a fixed resource allowlist, bounded results (500) and parameterized label search (`q`, maximum 80 characters). Every query scopes the tenant from the JWT, checks live membership/user status, and uses active campus scope. Teacher records are restricted to current timetable assignments, permission and subscription checks. Student/parent selections use existing verified portal links. Administrative employee/account/purchase choices remain administrator-only. Web and HRMS forward to this API through allowlisted BFF routes; no UI database access was introduced.

Dependent selections clear when their parent changes. Late responses are discarded after a campus change. Enrollment choices match the selected assigned class/year or assessment; optional attendance periods come from the assigned timetable. Lesson creation selects one complete teaching assignment, avoiding mismatched year/class/subject combinations. Search fields match names; they do not accept arbitrary IDs as record selections. Empty/error lists and required selections keep submit actions disabled until usable.

The offboarding activity checks current clearance after employee selection; the API still rechecks transactionally before changing employment/access.

## Verification

- `ExamManagementTests.AdmissionEnrollmentAttendanceFeesExamsResultsJourneyUsesOneStudent`: public admission → approval → registration/enrollment → daily attendance → fee assignment/payment/retry/receipt → marks → approval/publication → results; confirms one student's admission number throughout, zero remaining fee balance, idempotent payment replay, and foreign-campus result rejection.
- `ExamManagementTests.NamedChoicesRespectTenantCampusAndAssignedClassScope`: real MySQL lookup query coverage, assigned-class labels, verified-link names, foreign-campus and unprivileged-user denial, unknown-resource rejection.
- Portal rendering tests check that reference fields are selections rather than UUID inputs. UI tests cover dependency changes, stale responses, empty/error states and readable option labels.

The MySQL fixtures require `EDUVERSE_TEST_MYSQL`. GitHub Actions applies migrations 001–021 and verifies that this variable exists before running the integration suite. A local test run without that database is not evidence that the database journey executed. No new migration is required for these read projections.

Roadmap checkboxes reflect implemented development scopes; partial multi-part tasks retain unchecked boxes with explicit remaining work. Issues #18/#19 remain open for quality and operational acceptance evidence.
