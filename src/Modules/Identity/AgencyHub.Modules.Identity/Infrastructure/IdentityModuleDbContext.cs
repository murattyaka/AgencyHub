using AgencyHub.Infrastructure.Persistence;
using AgencyHub.Modules.Identity.Domain;
using AgencyHub.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AgencyHub.Modules.Identity.Infrastructure;

internal sealed class IdentityModuleDbContext : TenantAwareDbContext
{
    public const string Schema = "identity";

    public IdentityModuleDbContext(DbContextOptions<IdentityModuleDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<AgencyMembership> Memberships => Set<AgencyMembership>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("Users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Email).HasMaxLength(User.EmailMaxLength).IsRequired();
            b.Property(x => x.NormalizedEmail).HasMaxLength(User.EmailMaxLength).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(User.DisplayNameMaxLength).IsRequired();
            b.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            b.HasIndex(x => x.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<AgencyMembership>(b =>
        {
            b.ToTable("AgencyMemberships");
            b.HasKey(x => x.Id);
            b.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(x => new { x.AgencyId, x.UserId }).IsUnique();
            b.HasIndex(x => x.UserId);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.ToTable("RefreshTokens");
            b.HasKey(x => x.Id);
            b.Property(x => x.TokenHash).HasMaxLength(RefreshToken.TokenHashLength).IsFixedLength().IsUnicode(false);
            b.Property(x => x.RevocationReason).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => x.FamilyId);
            b.HasIndex(x => x.UserId);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
