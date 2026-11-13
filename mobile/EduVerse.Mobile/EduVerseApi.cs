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
public sealed record MobileTeacherClass(Guid ClassId, Guid CampusId, Guid AcademicYearId);
public sealed record MobileTeacherStudent(Guid StudentId, Guid EnrollmentId, string AdmissionNumber,
    string GivenName, string FamilyName);

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
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0)
                throw new ArgumentException("Enter a valid HTTPS API URL.");
            Preferences.Default.Set(UrlKey, uri.AbsoluteUri.TrimEnd('/') + "/");
        }
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
