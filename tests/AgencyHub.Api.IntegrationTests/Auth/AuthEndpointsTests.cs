using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgencyHub.Api.IntegrationTests.Infrastructure;
using AgencyHub.Modules.Identity.Contracts;
using AgencyHub.Modules.Identity.Endpoints;

namespace AgencyHub.Api.IntegrationTests.Auth;

public sealed class AuthEndpointsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AuthEndpointsTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_returns_access_token_and_sets_hardened_refresh_cookie()
    {
        var user = await _factory.CreateUserAsync(role: AgencyRole.Admin);
        using var client = _factory.CreateHttpsClient();

        var login = await client.LoginAsync(user);

        Assert.Equal(HttpStatusCode.OK, login.Response.StatusCode);
        Assert.NotNull(login.Body);
        Assert.Equal("Bearer", login.Body.TokenType);
        Assert.Equal(user.AgencyId, login.Body.AgencyId);
        Assert.Equal("Admin", login.Body.Role);
        Assert.False(string.IsNullOrWhiteSpace(login.Body.AccessToken));

        var rawCookie = TestUsers.ReadRawSetCookie(login.Response)!;
        Assert.Contains("httponly", rawCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", rawCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", rawCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"path={RefreshTokenCookie.Path}", rawCookie, StringComparison.OrdinalIgnoreCase);

        // The refresh token must never be readable from the response body.
        var body = await login.Response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(login.RefreshCookie!, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_are_indistinguishable()
    {
        var user = await _factory.CreateUserAsync();
        using var client = _factory.CreateHttpsClient();

        var unknown = await client.LoginAsync($"nobody-{Guid.NewGuid():N}@test.local", "Whatever-Password-1");
        var wrongPassword = await client.LoginAsync(user.Email, "Wrong-Password-123");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.Response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.Response.StatusCode);
        Assert.Equal(await ProblemSummaryAsync(unknown.Response), await ProblemSummaryAsync(wrongPassword.Response));
        Assert.Null(unknown.RefreshCookie);
        Assert.Null(wrongPassword.RefreshCookie);
    }

    [Fact]
    public async Task Login_with_missing_fields_returns_validation_problem()
    {
        using var client = _factory.CreateHttpsClient();

        var login = await client.LoginAsync(string.Empty, string.Empty);

        Assert.Equal(HttpStatusCode.BadRequest, login.Response.StatusCode);
        using var json = JsonDocument.Parse(await login.Response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("Email", out _));
    }

    [Fact]
    public async Task Email_is_case_insensitive()
    {
        var user = await _factory.CreateUserAsync();
        using var client = _factory.CreateHttpsClient();

        var login = await client.LoginAsync(user.Email.ToUpperInvariant(), user.Password);

        Assert.Equal(HttpStatusCode.OK, login.Response.StatusCode);
    }

    [Fact]
    public async Task Me_requires_a_token()
    {
        using var client = _factory.CreateHttpsClient();

        using var response = await client.GetMeAsync(accessToken: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_returns_user_of_the_token_and_ignores_tenant_headers()
    {
        var user = await _factory.CreateUserAsync(role: AgencyRole.Member);
        var otherAgency = Guid.NewGuid();
        using var client = _factory.CreateHttpsClient();
        var login = await client.LoginAsync(user);

        using var response = await client.GetMeAsync(login.Body!.AccessToken, r =>
        {
            r.Headers.Add("X-Agency-Id", otherAgency.ToString());
            r.RequestUri = new Uri($"/api/v1/auth/me?agencyId={otherAgency}", UriKind.Relative);
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(user.UserId, me!.UserId);
        Assert.Equal(user.AgencyId, me.AgencyId);
        Assert.Equal("Member", me.Role);
    }

    [Fact]
    public async Task Refresh_rotates_the_refresh_token_and_issues_a_working_access_token()
    {
        var user = await _factory.CreateUserAsync();
        using var client = _factory.CreateHttpsClient();
        var login = await client.LoginAsync(user);

        var refreshed = await client.RefreshAsync(login.RefreshCookie);

        Assert.Equal(HttpStatusCode.OK, refreshed.Response.StatusCode);
        Assert.NotNull(refreshed.RefreshCookie);
        Assert.NotEqual(login.RefreshCookie, refreshed.RefreshCookie);
        using var me = await client.GetMeAsync(refreshed.Body!.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_whole_family()
    {
        var user = await _factory.CreateUserAsync();
        using var client = _factory.CreateHttpsClient();
        var login = await client.LoginAsync(user);
        var rotated = await client.RefreshAsync(login.RefreshCookie);
        Assert.Equal(HttpStatusCode.OK, rotated.Response.StatusCode);

        // Attacker replays the stolen, already-used token.
        var replay = await client.RefreshAsync(login.RefreshCookie);
        // The legitimate user's newer token is now dead too.
        var legitimate = await client.RefreshAsync(rotated.RefreshCookie);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.Response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, legitimate.Response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-real-token")]
    public async Task Refresh_without_a_valid_cookie_is_rejected(string? cookie)
    {
        using var client = _factory.CreateHttpsClient();

        var refreshed = await client.RefreshAsync(cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.Response.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_and_clears_the_cookie()
    {
        var user = await _factory.CreateUserAsync();
        using var client = _factory.CreateHttpsClient();
        var login = await client.LoginAsync(user);

        var logout = await client.LogoutAsync(login.RefreshCookie);
        var refreshAfterLogout = await client.RefreshAsync(login.RefreshCookie);

        Assert.Equal(HttpStatusCode.NoContent, logout.Response.StatusCode);
        Assert.Contains("expires=Thu, 01 Jan 1970", TestUsers.ReadRawSetCookie(logout.Response)!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.Response.StatusCode);
    }

    [Fact]
    public async Task Multi_agency_user_must_choose_one_of_their_own_agencies()
    {
        var user = await _factory.CreateUserAsync();
        var secondAgency = Guid.NewGuid();
        await _factory.AddMembershipAsync(user.UserId, secondAgency, AgencyRole.Viewer);
        using var client = _factory.CreateHttpsClient();

        var withoutChoice = await client.LoginAsync(user);
        var ownAgency = await client.LoginAsync(user, secondAgency);
        var foreignAgency = await client.LoginAsync(user, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, withoutChoice.Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownAgency.Response.StatusCode);
        Assert.Equal(secondAgency, ownAgency.Body!.AgencyId);
        Assert.Equal("Viewer", ownAgency.Body.Role);
        Assert.Equal(HttpStatusCode.Forbidden, foreignAgency.Response.StatusCode);
        Assert.Null(foreignAgency.RefreshCookie);
    }

    [Fact]
    public async Task Deactivated_membership_loses_access_and_cannot_refresh()
    {
        var user = await _factory.CreateUserAsync();
        using var client = _factory.CreateHttpsClient();
        var login = await client.LoginAsync(user);

        await _factory.DeactivateMembershipAsync(user.UserId, user.AgencyId);

        using var me = await client.GetMeAsync(login.Body!.AccessToken);
        var refreshed = await client.RefreshAsync(login.RefreshCookie);
        var relogin = await client.LoginAsync(user);
        Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.Response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, relogin.Response.StatusCode);
    }

    private static async Task<string> ProblemSummaryAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return $"{json.RootElement.GetProperty("status")}|{json.RootElement.GetProperty("title")}|{json.RootElement.GetProperty("detail")}";
    }
}
