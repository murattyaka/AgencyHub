using AgencyHub.SharedKernel.Errors;
using AgencyHub.SharedKernel.Tenancy;

namespace AgencyHub.Infrastructure.Tenancy;

/// <summary>
/// Scoped tenant holder. One instance per HTTP request or per background job execution.
/// </summary>
public sealed class TenantContext : ITenantContext, ITenantContextSetter
{
    public Guid? AgencyId { get; private set; }

    public bool HasTenant => AgencyId.HasValue;

    public void SetTenant(Guid agencyId)
    {
        if (agencyId == Guid.Empty)
        {
            throw new ArgumentException("Agency id cannot be empty.", nameof(agencyId));
        }

        if (AgencyId is { } current && current != agencyId)
        {
            throw new TenantIsolationException("The tenant of a scope cannot be changed once it has been set.");
        }

        AgencyId = agencyId;
    }
}
