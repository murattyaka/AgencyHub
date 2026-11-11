using AgencyHub.Infrastructure.Persistence;
using AgencyHub.Modules.Identity.Domain;
using AgencyHub.Modules.Identity.Infrastructure;
using AgencyHub.SharedKernel.Errors;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgencyHub.Modules.Identity.Application;

/// <summary>Login, refresh-token rotation and sign-out.</summary>
/// <remarks>
/// Security properties (see SECURITY_CHECKLIST.md §2):
///  - One generic 401 for unknown user, wrong password, locked/disabled account and invalid tokens.
///  - Unknown users still pay the password-hash cost (timing-based user enumeration).
///  - Lockout after <see cref="MaxFailedSignInAttempts"/> failures for <see cref="LockoutDuration"/>.
///  - Refresh tokens are single use; presenting a rotated token revokes the whole family.
///  - Never log passwords or tokens.
/// </remarks>
internal sealed partial class AuthService
{
    internal const int MaxFailedSignInAttempts = 5;
    internal static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private const int MaxRefreshTokenLength = 128;

    private static readonly User TimingDummyUser = new("timing@invalid", "timing");

    private readonly IdentityModuleDbContext _db;
    private readonly TokenService _tokens;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<JwtOptions> _jwtOptions;
    private readonly ILogger<AuthService> _logger;
    private readonly Lazy<string> _timingDummyHash;

    public AuthService(
        IdentityModuleDbContext db,
        TokenService tokens,
        IPasswordHasher<User> passwordHasher,
        IValidator<LoginRequest> loginValidator,
        TimeProvider timeProvider,
        IOptions<JwtOptions> jwtOptions,
        ILogger<AuthService> logger)
    {
        _db = db;
        _tokens = tokens;
        _passwordHasher = passwordHasher;
        _loginValidator = loginValidator;
        _timeProvider = timeProvider;
        _jwtOptions = jwtOptions;
        _logger = logger;
        _timingDummyHash = new Lazy<string>(() => _passwordHasher.HashPassword(TimingDummyUser, Guid.NewGuid().ToString()));
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        await _loginValidator.ValidateAndThrowAsync(request, cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var normalizedEmail = User.NormalizeEmail(request.Email);
        var user = await _db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        if (user is null)
        {
            _passwordHasher.VerifyHashedPassword(TimingDummyUser, _timingDummyHash.Value, request.Password);
            LogSignInFailedUnknownUser(_logger);
            throw new AuthenticationFailedException();
        }

        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (!user.CanSignIn(now))
        {
            LogSignInRejected(_logger, user.Id);
            throw new AuthenticationFailedException();
        }

        if (verification == PasswordVerificationResult.Failed)
        {
            user.RegisterFailedSignIn(now, MaxFailedSignInAttempts, LockoutDuration);
            await _db.SaveChangesAsync(cancellationToken);
            LogSignInFailedWrongPassword(_logger, user.Id, user.IsLockedOut(now));
            throw new AuthenticationFailedException();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPasswordHash(_passwordHasher.HashPassword(user, request.Password));
        }

        user.RegisterSuccessfulSignIn();

        var membership = await ResolveMembershipAsync(user.Id, request.AgencyId, cancellationToken);
        var result = IssueTokens(user, membership, familyId: Guid.NewGuid(), predecessor: null, now);

        await _db.SaveChangesAsync(cancellationToken);
        LogSignInSucceeded(_logger, user.Id, membership.AgencyId);
        return result;
    }

    public async Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > MaxRefreshTokenLength)
        {
            throw new AuthenticationFailedException();
        }

        var now = _timeProvider.GetUtcNow();
        var tokenHash = TokenService.HashRefreshToken(refreshToken);
        var stored = await _db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken)
            ?? throw new AuthenticationFailedException();

        if (stored.IsRevoked)
        {
            if (stored.RevocationReason == RefreshTokenRevocationReason.Rotated)
            {
                // A token that was already exchanged is presented again: it was stolen or replayed.
                LogRefreshTokenReuse(_logger, stored.UserId, stored.FamilyId);
                await RevokeFamilyAsync(stored.FamilyId, RefreshTokenRevocationReason.ReuseDetected, now, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
            }

            throw new AuthenticationFailedException();
        }

        if (stored.IsExpired(now))
        {
            throw new AuthenticationFailedException();
        }

        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == stored.UserId, cancellationToken);
        var membership = await MembershipsOfUser(stored.UserId)
            .SingleOrDefaultAsync(m => m.AgencyId == stored.AgencyId, cancellationToken);

        if (user is null || !user.CanSignIn(now) || membership is null)
        {
            await RevokeFamilyAsync(stored.FamilyId, RefreshTokenRevocationReason.AccessRevoked, now, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            throw new AuthenticationFailedException();
        }

        var result = IssueTokens(user, membership, stored.FamilyId, predecessor: stored, now);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request rotated the same token concurrently; only one may win.
            LogConcurrentRefresh(_logger, stored.FamilyId);
            throw new AuthenticationFailedException();
        }

        return result;
    }

    public async Task SignOutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > MaxRefreshTokenLength)
        {
            return;
        }

        var tokenHash = TokenService.HashRefreshToken(refreshToken);
        var stored = await _db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
        if (stored is null)
        {
            return;
        }

        await RevokeFamilyAsync(stored.FamilyId, RefreshTokenRevocationReason.SignedOut, _timeProvider.GetUtcNow(), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<AgencyMembership> ResolveMembershipAsync(Guid userId, Guid? requestedAgencyId, CancellationToken cancellationToken)
    {
        var memberships = await MembershipsOfUser(userId).ToListAsync(cancellationToken);

        if (memberships.Count == 0)
        {
            throw new ForbiddenAccessException("The user has no active agency membership.");
        }

        if (requestedAgencyId is { } agencyId)
        {
            return memberships.SingleOrDefault(m => m.AgencyId == agencyId) ?? throw new ForbiddenAccessException();
        }

        return memberships.Count == 1
            ? memberships[0]
            : throw new BusinessRuleException("The user belongs to multiple agencies; specify agencyId.");
    }

    /// <summary>
    /// SECURITY REVIEWED: the tenant filter is bypassed because no tenant exists before sign-in.
    /// The query is restricted to the memberships of the user who has just proven their identity.
    /// </summary>
    private IQueryable<AgencyMembership> MembershipsOfUser(Guid userId) =>
        _db.Memberships
            .IgnoreQueryFilters([TenantAwareDbContext.TenantFilterName])
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.IsActive);

    private AuthResult IssueTokens(User user, AgencyMembership membership, Guid familyId, RefreshToken? predecessor, DateTimeOffset now)
    {
        var issuedRefresh = _tokens.CreateRefreshToken();
        var refreshToken = new RefreshToken(
            user.Id,
            membership.AgencyId,
            issuedRefresh.TokenHash,
            familyId,
            now,
            now.Add(_jwtOptions.Value.RefreshTokenLifetime));

        _db.RefreshTokens.Add(refreshToken);
        predecessor?.ReplaceWith(refreshToken, now);

        var access = _tokens.CreateAccessToken(user, membership);
        return new AuthResult(
            access.Token,
            access.ExpiresAtUtc,
            issuedRefresh.Token,
            refreshToken.ExpiresAtUtc,
            membership.AgencyId,
            membership.Role);
    }

    private async Task RevokeFamilyAsync(Guid familyId, RefreshTokenRevocationReason reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var activeTokens = await _db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.Revoke(now, reason);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sign-in succeeded for user {UserId} in agency {AgencyId}")]
    private static partial void LogSignInSucceeded(ILogger logger, Guid userId, Guid agencyId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in failed: unknown user")]
    private static partial void LogSignInFailedUnknownUser(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in failed: wrong password for user {UserId}; locked out: {LockedOut}")]
    private static partial void LogSignInFailedWrongPassword(ILogger logger, Guid userId, bool lockedOut);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in rejected: user {UserId} is locked out or disabled")]
    private static partial void LogSignInRejected(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 4031, Level = LogLevel.Warning, Message = "SECURITY: refresh token reuse detected for user {UserId}; token family {FamilyId} revoked")]
    private static partial void LogRefreshTokenReuse(ILogger logger, Guid userId, Guid familyId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Concurrent refresh rejected for token family {FamilyId}")]
    private static partial void LogConcurrentRefresh(ILogger logger, Guid familyId);
}
