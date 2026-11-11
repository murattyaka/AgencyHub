using AgencyHub.Api.IntegrationTests.Infrastructure;
using AgencyHub.Modules.Identity.Contracts;
using AgencyHub.Modules.Identity.Infrastructure;
using AgencyHub.SharedKernel.Errors;
using AgencyHub.SharedKernel.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgencyHub.Api.IntegrationTests.Auth;

public sealed class IdentityProvisioningTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public IdentityProvisioningTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Membership_is_created_for_the_current_tenant_and_password_is_hashed()
    {
        var user = await _factory.CreateUserAsync();

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(user.AgencyId);
        var db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        var membership = await db.Memberships.SingleAsync(m => m.UserId == user.UserId, TestContext.Current.CancellationToken);
        var stored = await db.Users.SingleAsync(u => u.Id == user.UserId, TestContext.Current.CancellationToken);

        Assert.Equal(user.AgencyId, membership.AgencyId);
        Assert.DoesNotContain(user.Password, stored.PasswordHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_case_insensitively()
    {
        var user = await _factory.CreateUserAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            ProvisionAsync(Guid.NewGuid(), new CreateAgencyUserRequest(user.Email.ToUpperInvariant(), "Dup", TestUsers.DefaultPassword, AgencyRole.Member)));
    }

    [Theory]
    [InlineData("short-pass1")]
    [InlineData("")]
    public async Task Weak_password_is_rejected(string password)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            ProvisionAsync(Guid.NewGuid(), new CreateAgencyUserRequest($"u-{Guid.NewGuid():N}@test.local", "Weak", password, AgencyRole.Member)));
    }

    [Fact]
    public async Task Provisioning_without_tenant_context_is_rejected()
    {
        await Assert.ThrowsAsync<TenantIsolationException>(() =>
            ProvisionAsync(agencyId: null, new CreateAgencyUserRequest($"u-{Guid.NewGuid():N}@test.local", "No tenant", TestUsers.DefaultPassword, AgencyRole.Member)));
    }

    private async Task ProvisionAsync(Guid? agencyId, CreateAgencyUserRequest request)
    {
        using var scope = _factory.Services.CreateScope();
        if (agencyId is { } id)
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(id);
        }

        await scope.ServiceProvider.GetRequiredService<IIdentityProvisioning>()
            .CreateUserWithMembershipAsync(request, TestContext.Current.CancellationToken);
    }
}
