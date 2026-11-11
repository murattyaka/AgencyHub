using AgencyHub.Modules.Identity.Contracts;
using System.Security.Claims;
using AgencyHub.Api.Security;
using AgencyHub.Infrastructure.Tenancy;
using AgencyHub.SharedKernel.Errors;
using Microsoft.AspNetCore.Http;

namespace AgencyHub.Api.IntegrationTests;

public sealed class TenantResolutionMiddlewareTests
{
    private static readonly Guid AgencyA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AgencyB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    [Fact]
    public async Task Authenticated_user_tenant_is_taken_from_claim()
    {
        var tenant = await RunAsync(Authenticated(new Claim(IdentityClaimTypes.AgencyId, AgencyA.ToString())));

        Assert.Equal(AgencyA, tenant.AgencyId);
    }

    [Fact]
    public async Task Tenant_headers_and_query_strings_are_ignored()
    {
        // Tenant id manipulation: client tries to switch tenant via request data.
        var tenant = await RunAsync(
            Authenticated(new Claim(IdentityClaimTypes.AgencyId, AgencyA.ToString())),
            ctx =>
            {
                ctx.Request.Headers["X-Agency-Id"] = AgencyB.ToString();
                ctx.Request.QueryString = new QueryString($"?agencyId={AgencyB}");
            });

        Assert.Equal(AgencyA, tenant.AgencyId);
    }

    [Fact]
    public async Task Unauthenticated_principal_never_gets_a_tenant_even_with_a_claim()
    {
        var unauthenticated = new ClaimsPrincipal(new ClaimsIdentity([new Claim(IdentityClaimTypes.AgencyId, AgencyA.ToString())]));

        var tenant = await RunAsync(unauthenticated);

        Assert.False(tenant.HasTenant);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Invalid_tenant_claim_is_rejected(string claimValue)
    {
        await Assert.ThrowsAsync<TenantIsolationException>(() =>
            RunAsync(Authenticated(new Claim(IdentityClaimTypes.AgencyId, claimValue))));
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));

    private static async Task<TenantContext> RunAsync(ClaimsPrincipal user, Action<HttpContext>? arrange = null)
    {
        var tenant = new TenantContext();
        var context = new DefaultHttpContext { User = user };
        arrange?.Invoke(context);
        var nextCalled = false;
        var middleware = new TenantResolutionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, tenant);

        Assert.True(nextCalled);
        return tenant;
    }
}
