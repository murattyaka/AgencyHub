using AgencyHub.Modules.Identity.Application;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AgencyHub.Api.IntegrationTests.Auth;

public sealed class JwtOptionsValidatorTests
{
    private const string StrongKey = "production-grade-signing-key-0123456789-abcdefghij";
    private const string DevKey = JwtOptions.DevelopmentKeyPrefix + "0123456789-abcdefghijklmnopqrstuvwxyz";

    [Fact]
    public void Valid_production_settings_pass()
    {
        Assert.True(Validate("Production", Options()).Succeeded);
    }

    [Fact]
    public void Development_key_is_rejected_outside_development()
    {
        Assert.True(Validate("Development", Options(key: DevKey)).Succeeded);
        Assert.True(Validate("Production", Options(key: DevKey)).Failed);
        Assert.True(Validate("Staging", Options(key: DevKey)).Failed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short-key")]
    public void Short_keys_are_rejected(string key)
    {
        Assert.True(Validate("Production", Options(key: key)).Failed);
    }

    [Fact]
    public void Missing_issuer_or_audience_is_rejected()
    {
        Assert.True(Validate("Production", Options(issuer: "")).Failed);
        Assert.True(Validate("Production", Options(audience: "")).Failed);
    }

    [Fact]
    public void Lifetimes_outside_safe_bounds_are_rejected()
    {
        Assert.True(Validate("Production", Options(access: TimeSpan.FromHours(8))).Failed);
        Assert.True(Validate("Production", Options(refresh: TimeSpan.FromDays(365))).Failed);
    }

    private static JwtOptions Options(
        string key = StrongKey,
        string issuer = "agencyhub-api",
        string audience = "agencyhub-web",
        TimeSpan? access = null,
        TimeSpan? refresh = null) => new()
        {
            SigningKey = key,
            Issuer = issuer,
            Audience = audience,
            AccessTokenLifetime = access ?? TimeSpan.FromMinutes(15),
            RefreshTokenLifetime = refresh ?? TimeSpan.FromDays(14),
        };

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(string environment, JwtOptions options) =>
        new JwtOptionsValidator(new TestHostEnvironment(environment)).Validate(name: null, options);

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "AgencyHub.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
