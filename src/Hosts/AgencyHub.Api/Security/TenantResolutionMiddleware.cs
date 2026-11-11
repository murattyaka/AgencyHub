using AgencyHub.Modules.Identity.Contracts;
using AgencyHub.SharedKernel.Errors;
using AgencyHub.SharedKernel.Tenancy;

namespace AgencyHub.Api.Security;

/// <summary>
/// Resolves the tenant ONLY from the authenticated principal's signed claims.
/// Request bodies, routes, query strings and headers are never trusted as a tenant source.
/// Must run after UseAuthentication().
/// </summary>
internal sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context, ITenantContextSetter tenantContextSetter)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var claimValue = user.FindFirst(IdentityClaimTypes.AgencyId)?.Value;
            if (claimValue is not null)
            {
                if (!Guid.TryParse(claimValue, out var agencyId) || agencyId == Guid.Empty)
                {
                    throw new TenantIsolationException("The tenant claim of the authenticated user is invalid.");
                }

                tenantContextSetter.SetTenant(agencyId);
            }
        }

        return _next(context);
    }
}
