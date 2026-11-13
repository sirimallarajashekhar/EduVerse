using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace EduVerse.Mobile;

public sealed record MobileLogin(string TenantSlug, string Email, string Password);
public sealed record MobileSession(string AccessToken, DateTimeOffset ExpiresUtc,
    string DisplayName, string Email, string SubjectId, Guid TenantId, string RoleCode);
public sealed record MobileLinkedStudent(Guid StudentId, Guid CampusId, string Relationship);
public sealed record MobileStudentProfile(Guid Id, Guid CampusId, string AdmissionNumber,
    string GivenName, string FamilyName, string Status);
public sealed record MobileAdmission(Guid Id, Guid TenantId, Guid CampusId, long ApplicationNumber,
    string GivenName, string FamilyName, DateOnly DateOfBirth, string Status);
public sealed record MobileStudentCreate(Guid CampusId, Guid? ApplicationId,
    string GivenName, string FamilyName, DateOnly DateOfBirth);
public sealed record MobileStudentRecord(Guid Id, Guid TenantId, Guid CampusId,
    string AdmissionNumber, string GivenName, string FamilyName, DateOnly DateOfBirth, string Status);
public sealed record MobileAdmissionDecision(string Decision);
public sealed record MobileGuardianCreate(string FullName, string Relationship,
    string? Phone, string? Email, bool IsPrimary);
public sealed record MobileGuardian(Guid Id, Guid StudentId, string FullName,
    string Relationship, string? Phone, string? Email, bool IsPrimary);
public sealed record MobileEnrollmentCreate(Guid AcademicYearId, Guid AcademicItemId);
public sealed record MobileEnrollment(Guid Id, Guid StudentId, Guid AcademicYearId,
    Guid AcademicItemId, string Status);
public sealed record MobileAcademicYear(Guid Id, Guid TenantId, Guid CampusId,
    string Code, string Name, DateOnly StartsOn, DateOnly EndsOn, bool IsActive);
public sealed record MobileAcademicItem(Guid Id, Guid TenantId, Guid CampusId,
    string Kind, string Code, string Name, Guid? ParentId, Guid? AcademicYearId,
    Guid? DepartmentId, DateOnly? StartsOn, DateOnly? EndsOn, bool IsActive);

public sealed record MobileCampus(Guid Id, Guid TenantId, string Code, string DisplayName, bool IsActive);
public sealed record MobileTeacherClass(Guid ClassId, Guid CampusId, Guid AcademicYearId);
public sealed record MobileTeacherStudent(Guid StudentId, Guid EnrollmentId, string AdmissionNumber,
    string GivenName, string FamilyName);
public sealed record MobileAttendanceMark(Guid CampusId, Guid EnrollmentId, DateOnly Date,
    Guid? PeriodId, string Status, string? Remarks);
public sealed record MobileAttendancePeriod(Guid Id, Guid CampusId, string Code, string Name,
    TimeOnly StartsAt, TimeOnly EndsAt, bool IsActive);

public sealed class EduVerseApi(HttpClient http)
{
    private const string TokenKey = "eduverse.access_token";
    private const string SessionKey = "eduverse.session";
    private const string UrlKey = "eduverse.api_url";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ApiUrl
    {
        get => Preferences.Default.Get(UrlKey, "");
        set
        {
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("Enter a valid HTTPS API URL without query or fragment.");
            var normalized = uri.AbsoluteUri.TrimEnd('/') + "/";
            var previous = Preferences.Default.Get(UrlKey, "");
            if (!string.Equals(previous, normalized, StringComparison.Ordinal))
            {
                // Sessions belong to their API endpoint. Never forward stored JWTs to another server.
                SecureStorage.Default.Remove(TokenKey);
                SecureStorage.Default.Remove(SessionKey);
            }
            Preferences.Default.Set(UrlKey, normalized);
        }
    }

    public async Task<string> CheckApiConnectionAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/v1/system/info", null, false, ct);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!document.RootElement.TryGetProperty("name", out var name) ||
            !string.Equals(name.GetString(), "EduVerse", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The configured server is not an EduVerse API.");
        return "Connected to EduVerse API.";
    }

    public async Task<MobileSession?> GetSessionAsync()
    {
        try
        {
            var token = await SecureStorage.Default.GetAsync(TokenKey);
            var raw = await SecureStorage.Default.GetAsync(SessionKey);
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(raw)) return null;
            var session = JsonSerializer.Deserialize<MobileSession>(raw, JsonOptions);
            if (session is null || session.ExpiresUtc <= DateTimeOffset.UtcNow)
            {
                await ClearLocalAsync();
                return null;
            }
            return session with { AccessToken = token };
        }
        catch (Exception)
        {
            await ClearLocalAsync();
            return null;
        }
    }

    public async Task<MobileSession> LoginAsync(MobileLogin login, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/v1/identity/login", login, false, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Invalid institution, email or password.");
        await EnsureSuccessAsync(response, ct);
        var session = await response.Content.ReadFromJsonAsync<MobileSession>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Sign-in returned no session.");
        await SecureStorage.Default.SetAsync(TokenKey, session.AccessToken);
        await SecureStorage.Default.SetAsync(SessionKey, JsonSerializer.Serialize(session with { AccessToken = "" }, JsonOptions));
        return session;
    }

    public async Task<JsonElement> GetAsync(string path, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, true, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
    }

    public async Task<IReadOnlyList<MobileLinkedStudent>> GetLinkedStudentsAsync(bool parent, CancellationToken ct = default)
    {
        var path = parent ? "api/v1/portals/parent/children" : "api/v1/portals/student/me";
        using var response = await SendAsync(HttpMethod.Get, path, null, true, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<List<MobileLinkedStudent>>(JsonOptions, ct)
            ?? throw new InvalidOperationException("The student list response was empty or invalid.");
    }

    public async Task<MobileStudentProfile?> GetMyStudentProfileAsync(
        Guid campusId, Guid studentId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"api/v1/ownership/my/students/{studentId:D}?campusId={campusId:D}", null, true, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<MobileStudentProfile>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Student profile response is invalid.");
    }

    public Task<JsonElement> GetInstitutionDashboardAsync(CancellationToken ct = default)
        => GetAsync("api/v1/institution-admin/dashboard", ct);

    public Task<JsonElement> GetInstitutionProfileAsync(CancellationToken ct = default)
        => GetAsync("api/v1/institution-admin/profile", ct);

    public Task<JsonElement> GetSubscriptionAsync(CancellationToken ct = default)
        => GetAsync("api/v1/institution-admin/subscription", ct);

    public Task<JsonElement> GetCampusStudentsAsync(Guid campusId, CancellationToken ct = default)
        => GetAsync($"api/v1/students?campusId={campusId:D}", ct);

    public Task<JsonElement> GetCampusApplicationsAsync(Guid campusId, CancellationToken ct = default)
        => GetAsync($"api/v1/students/applications?campusId={campusId:D}", ct);

    public async Task<IReadOnlyList<MobileAdmission>> GetAdmissionsAsync(Guid campusId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"api/v1/students/applications?campusId={campusId:D}", null, true, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<List<MobileAdmission>>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Admissions response is invalid.");
    }

    public async Task DecideAdmissionAsync(Guid campusId, Guid applicationId,
        string decision, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"api/v1/students/applications/{applicationId:D}/decision?campusId={campusId:D}",
            new MobileAdmissionDecision(decision), true, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<MobileStudentRecord> RegisterStudentAsync(MobileStudentCreate request,
        CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/v1/students", request, true, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<MobileStudentRecord>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Student registration response is invalid.");
    }

    public async Task<IReadOnlyList<MobileStudentRecord>> GetRegisteredStudentsAsync(
        Guid campusId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"api/v1/students?campusId={campusId:D}", null, true, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<List<MobileStudentRecord>>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Student list response is invalid.");
    }

    public async Task<IReadOnlyList<MobileGuardian>> GetGuardiansAsync(
        Guid campusId, Guid studentId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"api/v1/students/{studentId:D}/guardians?campusId={campusId:D}", null, true, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<List<MobileGuardian>>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Guardian list response is invalid.");
    }

    public async Task<MobileGuardian> AddGuardianAsync(Guid campusId, Guid studentId,
        MobileGuardianCreate guardian, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"api/v1/students/{studentId:D}/guardians?campusId={campusId:D}", guardian, true, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<MobileGuardian>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Guardian response is invalid.");
    }

    public async Task<IReadOnlyList<MobileAcademicYear>> GetAcademicYearsAsync(
        Guid campusId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"api/v1/academic/years?campusId={campusId:D}", null, true, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<List<MobileAcademicYear>>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Academic years response is invalid.");
    }

    public async Task<IReadOnlyList<MobileAcademicItem>> GetAcademicItemsAsync(
        Guid campusId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"api/v1/academic/catalog?campusId={campusId:D}", null, true, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<List<MobileAcademicItem>>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Academic catalog response is invalid.");
    }

    public async Task<IReadOnlyList<MobileEnrollment>> GetEnrollmentsAsync(
        Guid campusId, Guid studentId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"api/v1/students/{studentId:D}/enrollments?campusId={campusId:D}", null, true, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<List<MobileEnrollment>>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Enrollments response is invalid.");
    }

    public async Task<MobileEnrollment> EnrollStudentAsync(Guid campusId, Guid studentId,
        MobileEnrollmentCreate enrollment, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"api/v1/students/{studentId:D}/enrollments?campusId={campusId:D}", enrollment, true, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<MobileEnrollment>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Enrollment response is invalid.");
    }

    public async Task<IReadOnlyList<MobileCampus>> GetCampusesAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/v1/institutions/campuses", null, true, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<List<MobileCampus>>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Campus list response is invalid.");
    }

    public async Task<IReadOnlyList<MobileTeacherClass>> GetTeacherClassesAsync(Guid campusId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"api/v1/portals/teacher/classes?campusId={campusId:D}", null, true, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<List<MobileTeacherClass>>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Teacher classes response is invalid.");
    }

    public async Task<IReadOnlyList<MobileTeacherStudent>> GetTeacherRosterAsync(Guid campusId,
        Guid classId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"api/v1/portals/teacher/classes/{classId:D}/students?campusId={campusId:D}", null, true, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<List<MobileTeacherStudent>>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Teacher class roster response is invalid.");
    }

    public async Task<IReadOnlyList<MobileAttendancePeriod>> GetAttendancePeriodsAsync(
        Guid campusId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"api/v1/attendance/periods?campusId={campusId:D}", null, true, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<List<MobileAttendancePeriod>>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Attendance periods response is invalid.");
    }

    public async Task MarkTeacherAttendanceAsync(MobileAttendanceMark mark, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/v1/portals/teacher/attendance", mark, true, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task LogoutAsync()
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Post, "api/v1/identity/logout", new { }, true, default);
        }
        catch (Exception) { /* Always remove local credentials. */ }
        finally { await ClearLocalAsync(); }
    }

    private static Task ClearLocalAsync()
    {
        SecureStorage.Default.Remove(TokenKey);
        SecureStorage.Default.Remove(SessionKey);
        return Task.CompletedTask;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body,
        bool authenticated, CancellationToken ct)
    {
        var baseUrl = ApiUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new InvalidOperationException("Configure the HTTPS API URL first.");
        using var request = new HttpRequestMessage(method, new Uri(new Uri(baseUrl), path));
        if (authenticated)
        {
            var session = await GetSessionAsync();
            if (session is null) throw new UnauthorizedAccessException("Session expired. Sign in again.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        }
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        return await http.SendAsync(request, ct);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await ClearLocalAsync();
            throw new UnauthorizedAccessException("Session expired. Sign in again.");
        }
        if (response.StatusCode == HttpStatusCode.Forbidden) throw new UnauthorizedAccessException("You do not have permission to access this information.");
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"EduVerse API returned {(int)response.StatusCode}.", null, response.StatusCode);
        }
        await Task.CompletedTask;
    }
}
