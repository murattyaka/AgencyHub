using AgencyHub.Infrastructure.Persistence;
using AgencyHub.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AgencyHub.Modules.Identity.Infrastructure;

/// <summary>Used only by `dotnet ef`.</summary>
internal sealed class DesignTimeDbContextFactory : ModuleDesignTimeDbContextFactory<IdentityModuleDbContext>
{
    protected override string Schema => IdentityModuleDbContext.Schema;

    protected override IdentityModuleDbContext Create(DbContextOptions<IdentityModuleDbContext> options, TenantContext tenantContext) =>
        new(options, tenantContext);
}
