using AgencyHub.SharedKernel.Domain;

namespace AgencyHub.Modules.Identity.Domain;

/// <summary>A person who can sign in. Not tenant-owned: one user may belong to several agencies.</summary>
internal sealed class User : Entity, IAuditableEntity
{
    public const int EmailMaxLength = 256;
    public const int DisplayNameMaxLength = 100;

    private User()
    {
    }

    public User(string email, string displayName)
    {
        Email = email.Trim();
        NormalizedEmail = NormalizeEmail(email);
        DisplayName = displayName.Trim();
        IsActive = true;
    }

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public int AccessFailedCount { get; private set; }

    public DateTimeOffset? LockoutEndUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndUtc is { } end && end > now;

    public bool CanSignIn(DateTimeOffset now) => IsActive && !IsLockedOut(now);

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void RegisterFailedSignIn(DateTimeOffset now, int maxFailedAttempts, TimeSpan lockoutDuration)
    {
        AccessFailedCount++;
        if (AccessFailedCount >= maxFailedAttempts)
        {
            LockoutEndUtc = now.Add(lockoutDuration);
            AccessFailedCount = 0;
        }
    }

    public void RegisterSuccessfulSignIn()
    {
        AccessFailedCount = 0;
        LockoutEndUtc = null;
    }
}
