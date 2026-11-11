using AgencyHub.Infrastructure.Tenancy;
using AgencyHub.SharedKernel.Errors;

namespace AgencyHub.UnitTests.Tenancy;

public sealed class TenantContextTests
{
    private static readonly Guid AgencyA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AgencyB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    [Fact]
    public void New_context_has_no_tenant()
    {
        var context = new TenantContext();

        Assert.False(context.HasTenant);
        Assert.Null(context.AgencyId);
    }

    [Fact]
    public void Setting_the_same_tenant_twice_is_allowed()
    {
        var context = new TenantContext();

        context.SetTenant(AgencyA);
        context.SetTenant(AgencyA);

        Assert.Equal(AgencyA, context.AgencyId);
    }

    [Fact]
    public void Switching_tenant_within_a_scope_is_rejected()
    {
        var context = new TenantContext();
        context.SetTenant(AgencyA);

        Assert.Throws<TenantIsolationException>(() => context.SetTenant(AgencyB));
        Assert.Equal(AgencyA, context.AgencyId);
    }

    [Fact]
    public void Empty_tenant_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new TenantContext().SetTenant(Guid.Empty));
    }
}
