using AgencyHub.Modules.Identity.Contracts;
using AgencyHub.SharedKernel.Domain;
using AgencyHub.SharedKernel.Tenancy;

namespace AgencyHub.Modules.Identity.Domain;

/// <summary>Links a user to an agency with a role. Tenant-owned: AgencyId is assigned by the persistence layer.</summary>
internal sealed class AgencyMembership : Entity, ITenantOwned, IAuditableEntity
{
    private AgencyMembership()
    {
    }

    public AgencyMembership(Guid userId, AgencyRole role)
    {
        UserId = userId;
        Role = role;
        IsActive = true;
    }

    public Guid AgencyId { get; private set; }

    public Guid UserId { get; private set; }

    public AgencyRole Role { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public void Deactivate() => IsActive = false;
}
