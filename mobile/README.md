# EduVerse Mobile — first vertical slice

A native .NET 10 MAUI Android/iOS client for the existing EduVerse REST API. It does not connect to MySQL or implement business rules on-device.

## Implemented
- Editable HTTPS API base URL and tenant-slug/email/password sign-in against `POST /api/v1/identity/login`.
- SecureStorage-backed bearer token and session metadata with local expiration checks.
- Role-based home screen: parent children, student profile, teacher classes and lessons (teacher endpoints require campusId).
- Read-only API record views, logout via `POST /api/v1/identity/logout`, and access-denied messaging.
- No admin module editing or offline writes in this initial slice.

## Build
Install .NET 10 SDK, MAUI workload (`dotnet workload install maui`), and Android SDK/Android emulator. On Windows, build Android:
```powershell
dotnet build mobile/EduVerse.Mobile/EduVerse.Mobile.csproj -f net10.0-android
```
iOS requires a Mac with Xcode and compatible MAUI tooling. The API must be reachable from the phone over valid HTTPS; localhost on the phone is **not** your desktop.

## Security and next implementation
- Never embed JWT secrets, MySQL credentials or long-lived API credentials in the mobile application.
- This first slice uses the existing short-lived JWT login contract, not OIDC/PKCE or refresh tokens. Users must sign in again after expiration.
- Production release should add automated MAUI builds, app icons/splash assets, accessibility review, device tests, and backend-controlled campus discovery.
- Parent/student detail pages need a server-supported campus lookup and typed DTO mapping before attendance/results/timetable are enabled.
- For sensitive school records consider OS biometric unlock, logout-on-revocation behavior, and server-side security audits.
