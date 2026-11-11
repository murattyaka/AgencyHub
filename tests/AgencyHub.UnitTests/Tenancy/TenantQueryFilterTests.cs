using AgencyHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgencyHub.UnitTests.Tenancy;

public sealed class TenantQueryFilterTests : IDisposable
{
    private static readonly Guid AgencyA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AgencyB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly TenantTestDatabase _db = new();
    private readonly Guid _websiteOfB;

    public TenantQueryFilterTests()
    {
        using (var a = _db.CreateContext(AgencyA))
        {
            a.Websites.AddRange(new TestWebsite("https://a1.example"), new TestWebsite("https://a2.example"));
            a.SaveChanges();
        }

        using var b = _db.CreateContext(AgencyB);
        var website = new TestWebsite("https://b1.example");
        b.Websites.Add(website);
        b.SaveChanges();
        _websiteOfB = website.Id;
    }

    [Fact]
    public async Task Query_returns_only_rows_of_current_tenant()
    {
        await using var context = _db.CreateContext(AgencyA);

        var urls = await context.Websites.Select(w => w.Url).OrderBy(u => u).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["https://a1.example", "https://a2.example"], urls);
    }

    [Fact]
    public async Task Query_filter_is_evaluated_per_context_instance_although_model_is_cached()
    {
        // Same DbContext type => same cached model. Each instance must still see only its own tenant.
        await using var contextA = _db.CreateContext(AgencyA);
        await using var contextB = _db.CreateContext(AgencyB);

        var countA = await contextA.Websites.CountAsync(TestContext.Current.CancellationToken);
        var countB = await contextB.Websites.CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, countA);
        Assert.Equal(1, countB);
    }

    [Fact]
    public async Task Query_without_tenant_context_returns_nothing()
    {
        await using var context = _db.CreateContext(agencyId: null);

        Assert.Equal(0, await context.Websites.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Lookup_by_id_of_another_tenants_row_returns_null()
    {
        // IDOR scenario: tenant A guesses/knows the id of tenant B's website.
        await using var context = _db.CreateContext(AgencyA);

        var website = await context.Websites.FirstOrDefaultAsync(w => w.Id == _websiteOfB, TestContext.Current.CancellationToken);

        Assert.Null(website);
    }

    [Fact]
    public async Task Find_by_id_of_another_tenants_row_returns_null()
    {
        await using var context = _db.CreateContext(AgencyA);

        var website = await context.Websites.FindAsync([_websiteOfB], TestContext.Current.CancellationToken);

        Assert.Null(website);
    }

    [Fact]
    public async Task Ignoring_the_named_tenant_filter_returns_all_rows()
    {
        await using var context = _db.CreateContext(AgencyA);

        var count = await context.Websites
            .IgnoreQueryFilters([TenantAwareDbContext.TenantFilterName])
            .CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, count);
    }

    [Fact]
    public void Tenant_owned_entities_get_an_index_leading_with_AgencyId()
    {
        using var context = _db.CreateContext(AgencyA);
        var entityType = context.Model.FindEntityType(typeof(TestWebsite))!;

        Assert.Single(entityType.GetIndexes(), i => i.Properties[0].Name == "AgencyId");
    }

    public void Dispose() => _db.Dispose();
}
