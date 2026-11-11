using AgencyHub.Infrastructure.Persistence;
using AgencyHub.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;

namespace AgencyHub.UnitTests.Tenancy;

public sealed class TenantSaveChangesInterceptorTests : IDisposable
{
    private static readonly Guid AgencyA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AgencyB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly TenantTestDatabase _db = new();

    [Fact]
    public async Task Add_without_AgencyId_assigns_current_tenant()
    {
        await using var context = _db.CreateContext(AgencyA);
        var website = new TestWebsite("https://new.example");

        context.Websites.Add(website);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AgencyA, website.AgencyId);
    }

    [Fact]
    public async Task Add_with_AgencyId_of_another_tenant_is_rejected()
    {
        // Tenant id manipulation: attacker sets a foreign AgencyId on a new row.
        await using var context = _db.CreateContext(AgencyA);
        context.Websites.Add(TestWebsite.ForAgency(AgencyB, "https://evil.example"));

        await Assert.ThrowsAsync<TenantIsolationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Write_without_tenant_context_is_rejected()
    {
        await using var context = _db.CreateContext(agencyId: null);
        context.Websites.Add(new TestWebsite("https://orphan.example"));

        await Assert.ThrowsAsync<TenantIsolationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Synchronous_SaveChanges_is_guarded_too()
    {
        using var context = _db.CreateContext(AgencyA);
        context.Websites.Add(TestWebsite.ForAgency(AgencyB, "https://evil.example"));

        Assert.Throws<TenantIsolationException>(() => context.SaveChanges());
    }

    [Fact]
    public async Task Updating_a_row_of_another_tenant_is_rejected()
    {
        var foreignId = await SeedAsync(AgencyB, "https://b.example");

        await using var context = _db.CreateContext(AgencyA);
        var foreign = await LoadIgnoringTenantFilterAsync(context, foreignId);
        foreign.Url = "https://hijacked.example";

        await Assert.ThrowsAsync<TenantIsolationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Deleting_a_row_of_another_tenant_is_rejected()
    {
        var foreignId = await SeedAsync(AgencyB, "https://b.example");

        await using var context = _db.CreateContext(AgencyA);
        var foreign = await LoadIgnoringTenantFilterAsync(context, foreignId);
        context.Websites.Remove(foreign);

        await Assert.ThrowsAsync<TenantIsolationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Moving_own_row_to_another_tenant_is_rejected()
    {
        var ownId = await SeedAsync(AgencyA, "https://a.example");

        await using var context = _db.CreateContext(AgencyA);
        var own = await context.Websites.SingleAsync(w => w.Id == ownId, TestContext.Current.CancellationToken);
        context.Entry(own).Property(w => w.AgencyId).CurrentValue = AgencyB;

        await Assert.ThrowsAsync<TenantIsolationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Updating_own_row_succeeds()
    {
        var ownId = await SeedAsync(AgencyA, "https://a.example");

        await using (var context = _db.CreateContext(AgencyA))
        {
            var own = await context.Websites.SingleAsync(w => w.Id == ownId, TestContext.Current.CancellationToken);
            own.Url = "https://a-renamed.example";
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = _db.CreateContext(AgencyA);
        var reloaded = await verify.Websites.SingleAsync(w => w.Id == ownId, TestContext.Current.CancellationToken);
        Assert.Equal("https://a-renamed.example", reloaded.Url);
    }

    private async Task<Guid> SeedAsync(Guid agencyId, string url)
    {
        await using var context = _db.CreateContext(agencyId);
        var website = new TestWebsite(url);
        context.Websites.Add(website);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return website.Id;
    }

    private static Task<TestWebsite> LoadIgnoringTenantFilterAsync(TestDbContext context, Guid id) =>
        context.Websites
            .IgnoreQueryFilters([TenantAwareDbContext.TenantFilterName])
            .SingleAsync(w => w.Id == id, TestContext.Current.CancellationToken);

    public void Dispose() => _db.Dispose();
}
