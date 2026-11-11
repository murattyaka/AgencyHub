using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AgencyHub.Modules.Identity.Contracts;
using AgencyHub.Modules.Identity.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AgencyHub.Modules.Identity.Application;

internal sealed record IssuedAccessToken(string Token, DateTimeOffset ExpiresAtUtc);

internal sealed record IssuedRefreshToken(string Token, string TokenHash);

internal sealed class TokenService
{
    private const int RefreshTokenBytes = 32;

    private static readonly JsonWebTokenHandler Handler = new();

    private readonly IOptions<JwtOptions> _options;
    private readonly TimeProvider _timeProvider;

    public TokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    public static SymmetricSecurityKey CreateSigningKey(string signingKey) => new(Encoding.UTF8.GetBytes(signingKey));

    /// <summary>Only the hash is persisted. Hex (uppercase) SHA-256, 64 chars.</summary>
    public static string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    public IssuedAccessToken CreateAccessToken(User user, AgencyMembership membership)
    {
        var options = _options.Value;
        var now = _timeProvider.GetUtcNow();
        var expires = now.Add(options.AccessTokenLifetime);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [IdentityClaimTypes.UserId] = user.Id.ToString(),
                [IdentityClaimTypes.AgencyId] = membership.AgencyId.ToString(),
                [IdentityClaimTypes.Role] = membership.Role.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            },
            SigningCredentials = new SigningCredentials(CreateSigningKey(options.SigningKey), SecurityAlgorithms.HmacSha256),
        };

        return new IssuedAccessToken(Handler.CreateToken(descriptor), expires);
    }

#pragma warning disable CA1822 // Instance method keeps token creation behind the service for testability.
    public IssuedRefreshToken CreateRefreshToken()
#pragma warning restore CA1822
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RefreshTokenBytes));
        return new IssuedRefreshToken(token, HashRefreshToken(token));
    }
}
