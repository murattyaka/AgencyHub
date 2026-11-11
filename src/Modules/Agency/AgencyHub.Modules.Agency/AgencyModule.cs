using AgencyHub.Infrastructure.Persistence;
using AgencyHub.Modules.Agency.Application;
using AgencyHub.Modules.Agency.Infrastructure;
using AgencyHub.Modules.Identity.Contracts;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AgencyHub.Modules.Agency;

public static class AgencyModule
{
    public static IServiceCollection AddAgencyModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<AgencyModuleDbContext>(AgencyModuleDbContext.Schema);
        services.AddOptions<AgencyRegistrationOptions>().BindConfiguration(AgencyRegistrationOptions.SectionName);
        services.AddScoped<AgencyService>();
        services.AddValidatorsFromAssemblyContaining<AgencyService>(includeInternalTypes: true);
        return services;
    }

    /// <param name="endpoints">Route builder of the host.</param>
    /// <param name="registrationRateLimitPolicy">Strict rate-limit policy for the anonymous sign-up endpoint.</param>
    public static IEndpointRouteBuilder MapAgencyEndpoints(this IEndpointRouteBuilder endpoints, string registrationRateLimitPolicy)
    {
        endpoints.MapPost("/api/v1/agencies/register", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting(registrationRateLimitPolicy)
            .WithTags("Agency")
            .WithName("RegisterAgency");

        var current = endpoints.MapGroup("/api/v1/agency").WithTags("Agency");
        current.MapGet("/", GetCurrentAsync).RequireAuthorization(AuthorizationPolicies.AgencyMember).WithName("GetCurrentAgency");
        current.MapPut("/", UpdateCurrentAsync).RequireAuthorization(AuthorizationPolicies.AgencyAdmin).WithName("UpdateCurrentAgency");

        return endpoints;
    }

    private static async Task<Created<RegisterAgencyResult>> RegisterAsync(
        RegisterAgencyRequest request,
        AgencyService service,
        CancellationToken cancellationToken)
    {
        var result = await service.RegisterAsync(request, cancellationToken);
        return TypedResults.Created("/api/v1/agency", result);
    }

    private static async Task<Ok<AgencyResponse>> GetCurrentAsync(AgencyService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetCurrentAsync(cancellationToken));

    private static async Task<Ok<AgencyResponse>> UpdateCurrentAsync(
        UpdateAgencyRequest request,
        AgencyService service,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.UpdateCurrentAsync(request, cancellationToken));
}
