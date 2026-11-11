using Microsoft.AspNetCore.Http;

namespace AgencyHub.Modules.Identity.Endpoints;

/// <summary>
/// The refresh token never appears in a response body or in JavaScript-readable storage:
/// HttpOnly + Secure + SameSite=Strict cookie, scoped to the auth endpoints only. See DECISION_LOG ADR-011.
/// </summary>
internal static class RefreshTokenCookie
{
    public const string Name = "agencyhub_refresh";
    public const string Path = "/api/v1/auth";

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    public static void Write(HttpResponse response, string token, DateTimeOffset expiresAtUtc) =>
        response.Cookies.Append(Name, token, CreateOptions(expiresAtUtc));

    public static void Clear(HttpResponse response) => response.Cookies.Delete(Name, CreateOptions(expiresAtUtc: null));

    private static CookieOptions CreateOptions(DateTimeOffset? expiresAtUtc) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expiresAtUtc,
        IsEssential = true,
    };
}
