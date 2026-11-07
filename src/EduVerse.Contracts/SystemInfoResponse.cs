namespace EduVerse.Contracts;

/// <summary>Stable, versioned response contract for the foundation status endpoint.</summary>
public sealed record SystemInfoResponse(string Application, string ApiVersion, DateTimeOffset ServerTimeUtc);
