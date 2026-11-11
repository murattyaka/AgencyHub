using AgencyHub.Infrastructure.Persistence;
using AgencyHub.Infrastructure.Tenancy;
using AgencyHub.Modules.Website.Contracts;
using AgencyHub.Modules.Website.Domain;
using AgencyHub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AgencyHub.Modules.Website.Infrastructure;

internal sealed class WebsiteModuleDbContext : TenantAwareDbContext
{
    public const string Schema = "website";

    public WebsiteModuleDbContext(DbContextOptions<WebsiteModuleDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    public DbSet<Domain.Website> Websites => Set<Domain.Website>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Domain.Website>(b =>
        {
            b.ToTable("Websites");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(Domain.Website.NameMaxLength).IsRequired();
            b.Property(x => x.Url).HasMaxLength(WebsiteUrlPolicy.MaxLength).IsRequired();
            b.Property(x => x.Host).HasMaxLength(Domain.Website.HostMaxLength).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(x => new { x.AgencyId, x.Url }).IsUnique();
            b.HasIndex(x => new { x.AgencyId, x.ProjectId });
        });
    }
}

internal sealed class WebsiteDesignTimeDbContextFactory : ModuleDesignTimeDbContextFactory<WebsiteModuleDbContext>
{
    protected override string Schema => WebsiteModuleDbContext.Schema;

    protected override WebsiteModuleDbContext Create(DbContextOptions<WebsiteModuleDbContext> options, TenantContext tenantContext) =>
        new(options, tenantContext);
}

internal sealed class WebsiteDirectory : IWebsiteDirectory
{
    private readonly WebsiteModuleDbContext _db;

    public WebsiteDirectory(WebsiteModuleDbContext db)
    {
        _db = db;
    }

    public Task<bool> AnyForProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        _db.Websites.AnyAsync(w => w.ProjectId == projectId, cancellationToken);
}
