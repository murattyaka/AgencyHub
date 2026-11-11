using AgencyHub.SharedKernel.Domain;

namespace AgencyHub.Modules.Identity.Domain;

/// <summary>
/// A single-use refresh token. Only the SHA-256 hash of the token is stored.
/// Tokens issued from one login form a family; reuse of a rotated token revokes the whole family.
/// </summary>
/// <remarks>
/// Not <c>ITenantOwned</c>: refresh requests carry no access token, so no tenant context exists yet.
/// <see cref="AgencyId"/> records which agency the token was issued for and is re-validated on every refresh.
/// </remarks>
internal sealed class RefreshToken : Entity
{
    public const int TokenHashLength = 64;

    private RefreshToken()
    {
    }

    public RefreshToken(Guid userId, Guid agencyId, string tokenHash, Guid familyId, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        UserId = userId;
        AgencyId = agencyId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid UserId { get; private set; }

    public Guid AgencyId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public Guid FamilyId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public RefreshTokenRevocationReason? RevocationReason { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    /// <summary>Optimistic concurrency: two parallel refreshes with the same token cannot both succeed.</summary>
    public Guid ConcurrencyStamp { get; private set; } = Guid.NewGuid();

    public bool IsRevoked => RevokedAtUtc.HasValue;

    public bool IsExpired(DateTimeOffset now) => ExpiresAtUtc <= now;

    public void Revoke(DateTimeOffset now, RefreshTokenRevocationReason reason)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAtUtc = now;
        RevocationReason = reason;
        ConcurrencyStamp = Guid.NewGuid();
    }

    public void ReplaceWith(RefreshToken successor, DateTimeOffset now)
    {
        Revoke(now, RefreshTokenRevocationReason.Rotated);
        ReplacedByTokenId = successor.Id;
    }
}

internal enum RefreshTokenRevocationReason
{
    Rotated = 1,
    SignedOut = 2,
    ReuseDetected = 3,
    AccessRevoked = 4,
}
