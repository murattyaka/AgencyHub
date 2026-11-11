using System.Net;
using System.Text;
using AgencyHub.Api.IntegrationTests.Infrastructure;
using AgencyHub.Modules.Identity.Application;
using AgencyHub.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AgencyHub.Api.IntegrationTests.Auth;

/// <summary>Forged, tampered and expired access tokens must all be rejected with 401.</summary>
public sealed class AccessTokenValidationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AccessTokenValidationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private JwtOptions Jwt => _factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;

    [Fact]
    public async Task Changing_the_agency_claim_invalidates_the_signature()
    {
        // Tenant id manipulation inside the token itself.
        var user = await _factory.CreateUserAsync();
        using var client = _factory.CreateHttpsClient();
        var login = await client.LoginAsync(user);

        var parts = login.Body!.AccessToken.Split('.');
        var payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1]))
            .Replace(user.AgencyId.ToString(), Guid.NewGuid().ToString(), StringComparison.OrdinalIgnoreCase);
        var tampered = $"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}";

        await AssertRejectedAsync(client, tampered);
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        using var client = _factory.CreateHttpsClient();
        var token = CreateToken(signingKey: "attacker-controlled-key-0123456789-abcdefghijklmnop");

        await AssertRejectedAsync(client, token);
    }

    [Fact]
    public async Task Unsigned_alg_none_token_is_rejected()
    {
        using var client = _factory.CreateHttpsClient();
        var token = CreateToken(signingKey: null);

        await AssertRejectedAsync(client, token);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        using var client = _factory.CreateHttpsClient();
        var token = CreateToken(Jwt.SigningKey, notBefore: DateTime.UtcNow.AddHours(-2), expires: DateTime.UtcNow.AddMinutes(-5));

        await AssertRejectedAsync(client, token);
    }

    [Fact]
    public async Task Token_for_another_audience_is_rejected()
    {
        using var client = _factory.CreateHttpsClient();
        var token = CreateToken(Jwt.SigningKey, audience: "some-other-api");

        await AssertRejectedAsync(client, token);
    }

    [Fact]
    public async Task Correctly_signed_token_without_agency_claim_is_forbidden_by_fallback_policy()
    {
        using var client = _factory.CreateHttpsClient();
        var token = CreateToken(Jwt.SigningKey, includeAgency: false);

        using var response = await client.GetMeAsync(token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task AssertRejectedAsync(HttpClient client, string token)
    {
        using var response = await client.GetMeAsync(token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private string CreateToken(
        string? signingKey,
        string? audience = null,
        DateTime? notBefore = null,
        DateTime? expires = null,
        bool includeAgency = true)
    {
        var claims = new Dictionary<string, object>
        {
            [IdentityClaimTypes.UserId] = Guid.NewGuid().ToString(),
            [IdentityClaimTypes.Role] = nameof(AgencyRole.Owner),
        };
        if (includeAgency)
        {
            claims[IdentityClaimTypes.AgencyId] = Guid.NewGuid().ToString();
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Jwt.Issuer,
            Audience = audience ?? Jwt.Audience,
            IssuedAt = notBefore ?? DateTime.UtcNow,
            NotBefore = notBefore ?? DateTime.UtcNow,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(10),
            Claims = claims,
            SigningCredentials = signingKey is null
                ? null
                : new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
