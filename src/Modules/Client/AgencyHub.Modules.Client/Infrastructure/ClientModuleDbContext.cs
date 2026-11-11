using AgencyHub.Infrastructure.Persistence;
using AgencyHub.Infrastructure.Tenancy;
using AgencyHub.Modules.Client.Contracts;
using AgencyHub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AgencyHub.Modules.Client.Infrastructure;

internal sealed class ClientModuleDbContext : TenantAwareDbContext
{
    public const string Schema = "client";

    public ClientModuleDbContext(DbContextOptions<ClientModuleDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    public DbSet<Domain.Client> Clients => Set<Domain.Client>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Domain.Client>(b =>
        {
            b.ToTable("Clients");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(Domain.Client.NameMaxLength).IsRequired();
            b.Property(x => x.ContactName).HasMaxLength(Domain.Client.ContactNameMaxLength);
            b.Property(x => x.ContactEmail).HasMaxLength(Domain.Client.ContactEmailMaxLength);
            b.Property(x => x.Notes).HasMaxLength(Domain.Client.NotesMaxLength);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(x => new { x.AgencyId, x.Name });
        });
    }
}

internal sealed class ClientDesignTimeDbContextFactory : ModuleDesignTimeDbContextFactory<ClientModuleDbContext>
{
    protected override string Schema => ClientModuleDbContext.Schema;

    protected override ClientModuleDbContext Create(DbContextOptions<ClientModuleDbContext> options, TenantContext tenantContext) =>
        new(options, tenantContext);
}

internal sealed class ClientDirectory : IClientDirectory
{
    private readonly ClientModuleDbContext _db;

    public ClientDirectory(ClientModuleDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsAsync(Guid clientId, CancellationToken cancellationToken) =>
        _db.Clients.AnyAsync(c => c.Id == clientId, cancellationToken);
}
