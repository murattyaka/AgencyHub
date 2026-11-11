using AgencyHub.Infrastructure.Persistence;
using AgencyHub.Infrastructure.Tenancy;
using AgencyHub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AgencyHub.Modules.Agency.Infrastructure;

internal sealed class AgencyModuleDbContext : TenantAwareDbContext
{
    public const string Schema = "agency";

    public AgencyModuleDbContext(DbContextOptions<AgencyModuleDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    public DbSet<Domain.Agency> Agencies => Set<Domain.Agency>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Domain.Agency>(b =>
        {
            b.ToTable("Agencies");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(Domain.Agency.NameMaxLength).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });
    }
}

/// <summary>Used only by `dotnet ef`.</summary>
internal sealed class AgencyDesignTimeDbContextFactory : ModuleDesignTimeDbContextFactory<AgencyModuleDbContext>
{
    protected override string Schema => AgencyModuleDbContext.Schema;

    protected override AgencyModuleDbContext Create(DbContextOptions<AgencyModuleDbContext> options, TenantContext tenantContext) =>
        new(options, tenantContext);
}
