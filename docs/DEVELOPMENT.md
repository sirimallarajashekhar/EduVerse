# Local development

Prerequisites: .NET SDK 10, Git and an editor. MySQL is introduced in Phase 3 and is not needed yet.

Run from repository root:

```powershell
dotnet restore EduVerse.slnx
dotnet build EduVerse.slnx
dotnet test EduVerse.slnx
dotnet run --project src/EduVerse.Api --launch-profile EduVerse.Api
dotnet run --project src/EduVerse.Web --launch-profile EduVerse.Web
```

Run the last two commands in separate terminals. Open http://localhost:5200. The browser calls /bff/system/info (thin Razor app proxy), which forwards to http://localhost:5201/api/v1/system/info. Set Api__BaseUrl to configure the service address.

The Bootstrap CDN reference is for the starter shell only: pin and vendor production assets and adopt CSP before release. Never commit credentials or real student and employee data.

Use feature branches and reviewed PRs targeting main. Link relevant issues and include tests and security notes.
