using AgencyHub.UnitTests.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AgencyHub.UnitTests.Persistence;

public sealed class AuditableEntityInterceptorTests : IDisposable
{
    private static readonly Guid Agency = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Creator = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid Editor = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    private readonly TenantTestDatabase _db = new();

    [Fact]
    public async Task Created_and_updated_audit_fields_are_set_and_created_fields_cannot_be_overwritten()
    {
        var created = _db.Time.Now;
        Guid id;
        await using (var context = _db.CreateContext(Agency, Creator))
        {
            var website = new TestWebsite("https://a.example");
            context.Websites.Add(website);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            id = website.Id;

            Assert.Equal(created, website.CreatedAtUtc);
            Assert.Equal(Creator, website.CreatedBy);
            Assert.Null(website.UpdatedAtUtc);
        }

        var updated = created.AddHours(1);
        _db.Time.Now = updated;
        await using (var context = _db.CreateContext(Agency, Editor))
        {
            var website = await context.Websites.SingleAsync(w => w.Id == id, TestContext.Current.CancellationToken);
            website.Url = "https://b.example";
            context.Entry(website).Property(w => w.CreatedBy).CurrentValue = Editor; // tampering attempt
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = _db.CreateContext(Agency);
        var reloaded = await verify.Websites.SingleAsync(w => w.Id == id, TestContext.Current.CancellationToken);
        Assert.Equal(created, reloaded.CreatedAtUtc);
        Assert.Equal(Creator, reloaded.CreatedBy);
        Assert.Equal(updated, reloaded.UpdatedAtUtc);
        Assert.Equal(Editor, reloaded.UpdatedBy);
    }

    public void Dispose() => _db.Dispose();
}
