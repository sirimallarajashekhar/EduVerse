# EduVerse Mobile — Android device QA

.NET 10 MAUI Android/iOS client for the existing EduVerse REST API. The mobile app calls only authorized backend APIs; it does not directly access MySQL or duplicate backend business rules.

## Implemented workflows

- Institution slug/email/password sign-in against `POST /api/v1/identity/login`.
- HTTPS API URL and an unauthenticated **Test API connection** button using `GET /api/v1/system/info` to confirm an EduVerse server.
- SecureStorage JWT, expiry validation, logout and session-expired redirection.
- Parent: linked children, attendance, examination results and notices.
- Student: linked profile, attendance, results, timetable, assignments and notices.
- Teacher: API-provided campus selector, assigned classes, class roster, lessons and single-student attendance marking.

## Download the test APK

GitHub Actions workflow **EduVerse Mobile Android CI** produces a Debug ARM64 APK artifact when successful.

1. Open the most recent green Android CI workflow run in the repository's **Actions** tab.
2. Under **Artifacts**, download `eduverse-android-arm64-debug-apk`. GitHub login may be required.
3. Extract the ZIP. Install the resulting APK on a compatible **ARM64** Android test device (minimum Android 7.0 / API 24). Only install builds you trust. For testing, Android may ask you to allow this install source.
4. This is a **debug-only test artifact**, not a production-signed Google Play package. Artifact retention is 7 days; rerun CI if the artifact expires.

## Connect your phone to the API

- Run/deploy the EduVerse API so your Android device can reach it over **HTTPS** with a certificate trusted by Android.
- In the login screen enter the API base URL, e.g. `https://api.example.com/`, and tap **Test API connection**.
- A successful test confirms the EduVerse public system-info endpoint is reachable; it does **not** verify database readiness, login permissions or tenant access.
- `localhost` or `127.0.0.1` on a phone means **the phone**, not your development PC. For LAN or internet access, use a reachable address and valid HTTPS certificate. Do not disable TLS certificate validation.
- Never embed server signing keys, MySQL passwords, production JWTs or test credentials in the APK.

## Manual end-to-end test checklist

1. Invalid HTTPS URL, expired/untrusted certificate and unreachable API produce useful connection errors.
2. Correct institution slug and authorized account sign in; invalid credentials are rejected.
3. Parent and student accounts see only their own linked records; unrelated tenant/campus/student records must be denied.
4. Teacher loads available active campuses and sees only assigned classes and authorized rosters.
5. Teacher chooses an enrolled student, date and attendance status and saves via the server. Confirm the entry server-side using the existing authorized web/API view.
6. Test API errors 400/403/404/409, duplicate attendance writes, an academic holiday, and session revocation; no false success notification.
7. Logout clears session credentials and protected pages are no longer usable.
8. Repeat on a second Android device/Android version if possible.

Backend is authoritative for attendance date, role/campus entitlements, roster ownership, attendance validation and persistence. The client must not bypass these checks.

## Building locally

Install .NET 10 SDK, MAUI Android workload and Android SDK. From the repo root:

```powershell
dotnet workload install maui-android
dotnet build mobile/EduVerse.Mobile/EduVerse.Mobile.csproj -f net10.0-android -r android-arm64
```

iOS requires macOS, Xcode and a compatible MAUI workload. No iOS build or physical-device QA is claimed.
