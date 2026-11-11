using AgencyHub.SharedKernel.Errors;
using AgencyHub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgencyHub.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Write-side tenant guard:
///  - Added: assigns the current AgencyId when empty; rejects a different AgencyId.
///  - Modified/Deleted: rejects rows of another agency and any change of AgencyId.
///  - Any tenant-owned write without a tenant context is rejected.
/// </summary>
/// <remarks>
/// ExecuteUpdate/ExecuteDelete and raw SQL bypass SaveChanges interceptors. Query filters still
/// restrict ExecuteUpdate/ExecuteDelete, but they must never set AgencyId (see SECURITY_CHECKLIST.md).
/// </remarks>
public sealed class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenantContext;

    public TenantSaveChangesInterceptor(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        EnforceTenantIsolation(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        EnforceTenantIsolation(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void EnforceTenantIsolation(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            var currentAgencyId = _tenantContext.AgencyId
                ?? throw new TenantIsolationException(
                    $"Cannot write '{entry.Metadata.DisplayName()}' without a tenant context.");

            var agencyProperty = entry.Property<Guid>(nameof(ITenantOwned.AgencyId));

            switch (entry.State)
            {
                case EntityState.Added:
                    if (agencyProperty.CurrentValue == Guid.Empty)
                    {
                        agencyProperty.CurrentValue = currentAgencyId;
                    }
                    else if (agencyProperty.CurrentValue != currentAgencyId)
                    {
                        throw CrossTenantWrite(entry.Metadata.DisplayName());
                    }

                    break;

                case EntityState.Modified:
                case EntityState.Deleted:
                    if (agencyProperty.OriginalValue != currentAgencyId || agencyProperty.CurrentValue != currentAgencyId)
                    {
                        throw CrossTenantWrite(entry.Metadata.DisplayName());
                    }

                    break;
            }
        }
    }

    private static TenantIsolationException CrossTenantWrite(string entityName) =>
        new($"Cross-tenant write attempt on '{entityName}' was blocked.");
}
