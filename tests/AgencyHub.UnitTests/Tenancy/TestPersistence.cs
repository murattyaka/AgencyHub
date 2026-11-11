using AgencyHub.Infrastructure.Persistence;
using AgencyHub.Infrastructure.Persistence.Interceptors;
using AgencyHub.Infrastructure.Tenancy;
using AgencyHub.SharedKernel.Domain;
using AgencyHub.SharedKernel.Security;
using AgencyHub.SharedKernel.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AgencyHub.UnitTests.Tenancy;

internal sealed class TestWebsite : Entity, ITenantOwned, IAuditableEntity
{
    public TestWebsite(string url)
    {
        Url = url;
    }

    public Guid AgencyId { get; private set; }

    public string Url { get; set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static TestWebsite ForAgency(Guid agencyId, string url) => new(url) { AgencyId = agencyId };
}

internal sealed class TestDbContext : TenantAwareDbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    public DbSet<TestWebsite> Websites => Set<TestWebsite>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<TestWebsite>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Url).HasMaxLength(200);
        });
}

internal sealed class StubCurrentUser(Guid? userId) : ICurrentUser
{
    public Guid? UserId { get; } = userId;
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Shared in-memory SQLite database. Each context gets its own tenant context, like separate HTTP requests.
/// </summary>
internal sealed class TenantTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public TenantTestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var context = CreateContext(agencyId: null);
        context.Database.EnsureCreated();
    }

    public FixedTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero));

    public TestDbContext CreateContext(Guid? agencyId, Guid? userId = null)
    {
        var tenant = new TenantContext();
        if (agencyId is { } id)
        {
            tenant.SetTenant(id);
        }

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(
                new TenantSaveChangesInterceptor(tenant),
                new AuditableEntityInterceptor(Time, new StubCurrentUser(userId)))
            .Options;

        return new TestDbContext(options, tenant);
    }

    public void Dispose() => _connection.Dispose();
}
