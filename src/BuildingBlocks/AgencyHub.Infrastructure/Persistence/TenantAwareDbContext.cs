using System.Reflection;
using AgencyHub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AgencyHub.Infrastructure.Persistence;

/// <summary>
/// Base class for every module DbContext. Applies a named global query filter to all
/// <see cref="ITenantOwned"/> entities so reads are restricted to the current agency.
/// </summary>
/// <remarks>
/// Fail-closed: when no tenant is set, tenant-owned queries return nothing.
/// Platform-level code that legitimately needs cross-tenant reads must call
/// <c>IgnoreQueryFilters([TenantFilterName])</c> explicitly; such call sites are reviewed (SECURITY_CHECKLIST.md).
/// </remarks>
public abstract class TenantAwareDbContext : DbContext
{
    public const string TenantFilterName = "Tenant";

    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(TenantAwareDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Tenant filter method not found.");

    private readonly ITenantContext _tenantContext;

    protected TenantAwareDbContext(DbContextOptions options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Referenced by the query filters. EF Core evaluates it per query against the executing
    /// context instance, so the cached model is safe to share between tenants.
    /// </summary>
    protected Guid? CurrentAgencyId => _tenantContext.AgencyId;

    /// <summary>
    /// Sealed so derived contexts cannot skip the tenant filters. Configure entities in <see cref="ConfigureModel"/>.
    /// </summary>
    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureModel(modelBuilder);

        var tenantOwnedRootTypes = modelBuilder.Model.GetEntityTypes()
            .Where(t => t.BaseType is null && !t.IsOwned() && typeof(ITenantOwned).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType)
            .ToList();

        foreach (var clrType in tenantOwnedRootTypes)
        {
            ApplyTenantFilterMethod.MakeGenericMethod(clrType).Invoke(this, [modelBuilder]);
        }
    }

    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned
    {
        var entity = modelBuilder.Entity<TEntity>();
        entity.HasQueryFilter(TenantFilterName, e => e.AgencyId == CurrentAgencyId);

        // Every tenant query filters on AgencyId; add an index unless a configured index already leads with it.
        var hasLeadingAgencyIndex = entity.Metadata.GetIndexes()
            .Any(i => i.Properties[0].Name == nameof(ITenantOwned.AgencyId));
        if (!hasLeadingAgencyIndex)
        {
            entity.HasIndex(e => e.AgencyId);
        }
    }
}
