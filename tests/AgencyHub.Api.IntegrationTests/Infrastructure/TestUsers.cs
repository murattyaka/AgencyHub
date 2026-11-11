using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgencyHub.Modules.Identity.Contracts;
using AgencyHub.Modules.Identity.Domain;
using AgencyHub.Modules.Identity.Endpoints;
using AgencyHub.Modules.Identity.Infrastructure;
using AgencyHub.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgencyHub.Api.IntegrationTests.Infrastructure;

public sealed record TestUser(Guid UserId, string Email, string Password, Guid AgencyId);

internal sealed record LoginOutcome(HttpResponseMessage Response, AuthResponse? Body, string? RefreshCookie);

/// <summary>Seeds users through the real provisioning contract and drives the auth endpoints.</summary>
internal static class TestUsers
{
    public const string DefaultPassword = "Correct-Horse-Battery-9";

    public static async Task<TestUser> CreateUserAsync(
        this WebApplicationFactory<Program> factory,
        Guid? agencyId = null,
        AgencyRole role = AgencyRole.Owner,
        string password = DefaultPassword)
    {
        var agency = agencyId ?? Guid.NewGuid();
        var email = $"user-{Guid.NewGuid():N}@test.local";

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(agency);
        var userId = await scope.ServiceProvider.GetRequiredService<IIdentityProvisioning>()
            .CreateUserWithMembershipAsync(new CreateAgencyUserRequest(email, "Test User", password, role), TestContext.Current.CancellationToken);

        return new TestUser(userId, email, password, agency);
    }

    public static async Task AddMembershipAsync(this WebApplicationFactory<Program> factory, Guid userId, Guid agencyId, AgencyRole role = AgencyRole.Member)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(agencyId);
        var db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        db.Memberships.Add(new AgencyMembership(userId, role));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public static async Task DeactivateMembershipAsync(this WebApplicationFactory<Program> factory, Guid userId, Guid agencyId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(agencyId);
        var db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        var membership = await db.Memberships.SingleAsync(m => m.UserId == userId, TestContext.Current.CancellationToken);
        membership.Deactivate();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>HTTPS (so Secure cookies are valid) and manual cookie handling (so tests control which token is sent).</summary>
    public static HttpClient CreateHttpsClient(this WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

    public static async Task<LoginOutcome> LoginAsync(this HttpClient client, string email, string password, Guid? agencyId = null)
    {
        var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new { email, password, agencyId },
            TestContext.Current.CancellationToken);

        var body = response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<AuthResponse>(TestContext.Current.CancellationToken)
            : null;

        return new LoginOutcome(response, body, ReadRefreshCookie(response));
    }

    public static Task<LoginOutcome> LoginAsync(this HttpClient client, TestUser user, Guid? agencyId = null) =>
        client.LoginAsync(user.Email, user.Password, agencyId);

    public static async Task<LoginOutcome> RefreshAsync(this HttpClient client, string? refreshCookie) =>
        await SendWithCookieAsync(client, "/api/v1/auth/refresh", refreshCookie);

    public static async Task<LoginOutcome> LogoutAsync(this HttpClient client, string? refreshCookie) =>
        await SendWithCookieAsync(client, "/api/v1/auth/logout", refreshCookie);

    public static async Task<HttpResponseMessage> GetMeAsync(this HttpClient client, string? accessToken, Action<HttpRequestMessage>? arrange = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/auth/me", UriKind.Relative));
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        arrange?.Invoke(request);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static string? ReadRefreshCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        var prefix = RefreshTokenCookie.Name + "=";
        var cookie = cookies.FirstOrDefault(c => c.StartsWith(prefix, StringComparison.Ordinal));
        var value = cookie?[prefix.Length..].Split(';')[0];
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public static string? ReadRawSetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(c => c.StartsWith(RefreshTokenCookie.Name + "=", StringComparison.Ordinal))
            : null;

    private static async Task<LoginOutcome> SendWithCookieAsync(HttpClient client, string path, string? refreshCookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        if (refreshCookie is not null)
        {
            request.Headers.Add("Cookie", $"{RefreshTokenCookie.Name}={refreshCookie}");
        }

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = response.IsSuccessStatusCode && response.Content.Headers.ContentLength != 0
            ? await response.Content.ReadFromJsonAsync<AuthResponse>(TestContext.Current.CancellationToken)
            : null;

        return new LoginOutcome(response, body, ReadRefreshCookie(response));
    }
}
