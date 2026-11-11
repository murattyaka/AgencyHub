using AgencyHub.Modules.Identity.Application;
using AgencyHub.Modules.Identity.Contracts;
using AgencyHub.Modules.Identity.Infrastructure;
using AgencyHub.SharedKernel.Errors;
using AgencyHub.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace AgencyHub.Modules.Identity.Endpoints;

internal sealed record AuthResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, Guid AgencyId, string Role);

internal sealed record CurrentUserResponse(Guid UserId, string Email, string DisplayName, Guid AgencyId, string Role);

internal static class IdentityEndpoints
{
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/v1/auth").WithTags("Auth");

        auth.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityModule.AuthenticationRateLimitPolicy)
            .WithName("Login");

        auth.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityModule.AuthenticationRateLimitPolicy)
            .WithName("RefreshToken");

        auth.MapPost("/logout", LogoutAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityModule.AuthenticationRateLimitPolicy)
            .WithName("Logout");

        auth.MapGet("/me", GetCurrentUserAsync)
            .RequireAuthorization(AuthorizationPolicies.AgencyMember)
            .WithName("GetCurrentUser");

        return endpoints;
    }

    private static async Task<Ok<AuthResponse>> LoginAsync(
        LoginRequest request,
        AuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);
        return TypedResults.Ok(Complete(httpContext, result));
    }

    private static async Task<Ok<AuthResponse>> RefreshAsync(
        AuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // On failure the exception handler resets the response (headers included), so the stale cookie stays
        // in the browser; it is harmless because the token is unusable. The client signs in again.
        var result = await authService.RefreshAsync(RefreshTokenCookie.Read(httpContext.Request), cancellationToken);
        return TypedResults.Ok(Complete(httpContext, result));
    }

    private static async Task<NoContent> LogoutAsync(
        AuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        await authService.SignOutAsync(RefreshTokenCookie.Read(httpContext.Request), cancellationToken);
        RefreshTokenCookie.Clear(httpContext.Response);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<CurrentUserResponse>> GetCurrentUserAsync(
        ICurrentUser currentUser,
        IdentityModuleDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException();

        var user = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken)
            ?? throw new AuthenticationFailedException();

        // Tenant query filter limits this to the agency in the token; a deactivated membership loses access immediately.
        var membership = await db.Memberships.AsNoTracking()
            .SingleOrDefaultAsync(m => m.UserId == userId && m.IsActive, cancellationToken)
            ?? throw new ForbiddenAccessException();

        return TypedResults.Ok(new CurrentUserResponse(user.Id, user.Email, user.DisplayName, membership.AgencyId, membership.Role.ToString()));
    }

    private static AuthResponse Complete(HttpContext httpContext, AuthResult result)
    {
        RefreshTokenCookie.Write(httpContext.Response, result.RefreshToken, result.RefreshTokenExpiresAtUtc);
        return new AuthResponse(result.AccessToken, "Bearer", result.AccessTokenExpiresAtUtc, result.AgencyId, result.Role.ToString());
    }
}
