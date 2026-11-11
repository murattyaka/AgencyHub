using AgencyHub.Infrastructure.Persistence;
using AgencyHub.Infrastructure.Tenancy;
using AgencyHub.Modules.Project.Contracts;
using AgencyHub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AgencyHub.Modules.Project.Infrastructure;

internal sealed class ProjectModuleDbContext : TenantAwareDbContext
{
    public const string Schema = "project";

    public ProjectModuleDbContext(DbContextOptions<ProjectModuleDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    public DbSet<Domain.Project> Projects => Set<Domain.Project>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Domain.Project>(b =>
        {
            b.ToTable("Projects");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(Domain.Project.NameMaxLength).IsRequired();
            b.Property(x => x.Description).HasMaxLength(Domain.Project.DescriptionMaxLength);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(x => new { x.AgencyId, x.ClientId });
            b.HasIndex(x => new { x.AgencyId, x.Name });
        });
    }
}

internal sealed class ProjectDesignTimeDbContextFactory : ModuleDesignTimeDbContextFactory<ProjectModuleDbContext>
{
    protected override string Schema => ProjectModuleDbContext.Schema;

    protected override ProjectModuleDbContext Create(DbContextOptions<ProjectModuleDbContext> options, TenantContext tenantContext) =>
        new(options, tenantContext);
}

internal sealed class ProjectDirectory : IProjectDirectory
{
    private readonly ProjectModuleDbContext _db;

    public ProjectDirectory(ProjectModuleDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsAsync(Guid projectId, CancellationToken cancellationToken) =>
        _db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken);

    public Task<bool> AnyForClientAsync(Guid clientId, CancellationToken cancellationToken) =>
        _db.Projects.AnyAsync(p => p.ClientId == clientId, cancellationToken);
}
