using System.Net;
using AgencyHub.Api.IntegrationTests.Infrastructure;
using AgencyHub.Modules.Identity.Application;
using Microsoft.Extensions.DependencyInjection;

namespace AgencyHub.Api.IntegrationTests.Auth;

/// <summary>Each test owns its factory: lockout manipulates the clock, rate limiting uses production limits.</summary>
public sealed class BruteForceProtectionTests
{
    private readonly SqlServerTestDatabase _database;

    public BruteForceProtectionTests(SqlServerTestDatabase database)
    {
        _database = database;
    }

    [Fact]
    public async Task Account_is_locked_after_repeated_failures_and_unlocks_after_the_lockout_period()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = new ApiFactory(
            _database.ConnectionString,
            configureTestServices: services => services.AddSingleton<TimeProvider>(clock));
        var user = await factory.CreateUserAsync();
        using var client = factory.CreateHttpsClient();

        for (var attempt = 0; attempt < AuthService.MaxFailedSignInAttempts; attempt++)
        {
            var failed = await client.LoginAsync(user.Email, "Wrong-Password-123");
            Assert.Equal(HttpStatusCode.Unauthorized, failed.Response.StatusCode);
        }

        var correctWhileLocked = await client.LoginAsync(user);
        Assert.Equal(HttpStatusCode.Unauthorized, correctWhileLocked.Response.StatusCode);

        clock.Advance(AuthService.LockoutDuration + TimeSpan.FromSeconds(1));

        var correctAfterLockout = await client.LoginAsync(user);
        Assert.Equal(HttpStatusCode.OK, correctAfterLockout.Response.StatusCode);
    }

    [Fact]
    public async Task Login_is_rate_limited_per_client_with_retry_after()
    {
        const int permitLimit = 5;
        await using var factory = new ApiFactory(
            _database.ConnectionString,
            settings: new Dictionary<string, string?> { ["RateLimiting:AuthenticationPermitLimitPerMinute"] = permitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        using var client = factory.CreateHttpsClient();

        for (var attempt = 0; attempt < permitLimit; attempt++)
        {
            var allowed = await client.LoginAsync($"nobody-{attempt}@test.local", "Whatever-Password-1");
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.Response.StatusCode);
        }

        var limited = await client.LoginAsync("nobody@test.local", "Whatever-Password-1");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.Response.StatusCode);
        Assert.True(limited.Response.Headers.RetryAfter is not null || limited.Response.Headers.Contains("Retry-After"));
    }
}
