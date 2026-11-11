namespace AgencyHub.Modules.Identity.Contracts;

/// <summary>Role of a user inside one agency. Stored as string; do not renumber.</summary>
public enum AgencyRole
{
    Owner = 1,
    Admin = 2,
    Member = 3,
    Viewer = 4,
}

/// <summary>Claim types issued in access tokens. Consumers must use these constants, never literals.</summary>
public static class IdentityClaimTypes
{
    public const string UserId = "sub";
    public const string AgencyId = "agency_id";
    public const string Role = "role";
}

/// <summary>Authorization policy names. Every business endpoint requires at least <see cref="AgencyMember"/> (fallback policy).</summary>
public static class AuthorizationPolicies
{
    /// <summary>Authenticated and bound to an agency (token carries agency_id).</summary>
    public const string AgencyMember = "AgencyMember";

    /// <summary>May create and edit agency data: Owner, Admin or Member (not Viewer).</summary>
    public const string AgencyEditor = "AgencyEditor";

    /// <summary>Owner or Admin of the current agency: settings, deletions, user management.</summary>
    public const string AgencyAdmin = "AgencyAdmin";
}

public sealed record CreateAgencyUserRequest(string Email, string DisplayName, string Password, AgencyRole Role);

/// <summary>
/// Used by other modules (e.g. Agency registration, user invitations) to create users.
/// Must be called inside a scope whose tenant is set; the membership is created for that tenant.
/// </summary>
public interface IIdentityProvisioning
{
    /// <returns>The new user's id.</returns>
    /// <remarks>
    /// Throws FluentValidation.ValidationException for invalid input (email, display name, weak password)
    /// and <see cref="SharedKernel.Errors.ConflictException"/> when a user with the same email already exists.
    /// </remarks>
    Task<Guid> CreateUserWithMembershipAsync(CreateAgencyUserRequest request, CancellationToken cancellationToken);
}
