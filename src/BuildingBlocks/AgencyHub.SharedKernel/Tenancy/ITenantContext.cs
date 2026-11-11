namespace AgencyHub.SharedKernel.Tenancy;

/// <summary>
/// The agency (tenant) the current unit of work executes for.
/// Resolved from the authenticated token in HTTP requests and set explicitly in background jobs.
/// Never resolved from request bodies, query strings, routes or headers.
/// </summary>
public interface ITenantContext
{
    Guid? AgencyId { get; }

    bool HasTenant { get; }
}

/// <summary>
/// Sets the tenant for the current DI scope. Can be set once per scope; switching to a
/// different tenant within the same scope is rejected.
/// </summary>
public interface ITenantContextSetter
{
    void SetTenant(Guid agencyId);
}
