using AgencyHub.Infrastructure.Persistence;
using AgencyHub.Modules.Identity.Contracts;
using AgencyHub.Modules.Website.Application;
using AgencyHub.Modules.Website.Contracts;
using AgencyHub.Modules.Website.Infrastructure;
using AgencyHub.SharedKernel.Pagination;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AgencyHub.Modules.Website;

public static class WebsiteModule
{
    public static IServiceCollection AddWebsiteModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<WebsiteModuleDbContext>(WebsiteModuleDbContext.Schema);
        services.AddScoped<WebsiteService>();
        services.AddScoped<IWebsiteDirectory, WebsiteDirectory>();
        services.AddValidatorsFromAssemblyContaining<WebsiteService>(includeInternalTypes: true);
        return services;
    }

    public static IEndpointRouteBuilder MapWebsiteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/websites").WithTags("Websites");

        group.MapGet("/", ListAsync).RequireAuthorization(AuthorizationPolicies.AgencyMember).WithName("ListWebsites");
        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization(AuthorizationPolicies.AgencyMember).WithName("GetWebsite");
        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.AgencyEditor).WithName("CreateWebsite");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.AgencyEditor).WithName("UpdateWebsite");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.AgencyAdmin).WithName("DeleteWebsite");

        return endpoints;
    }

    private static async Task<Ok<PagedResult<WebsiteResponse>>> ListAsync(
        [AsParameters] WebsiteListQuery query, WebsiteService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ListAsync(query, cancellationToken));

    private static async Task<Ok<WebsiteResponse>> GetAsync(Guid id, WebsiteService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAsync(id, cancellationToken));

    private static async Task<Created<WebsiteResponse>> CreateAsync(
        CreateWebsiteRequest request, WebsiteService service, CancellationToken cancellationToken)
    {
        var website = await service.CreateAsync(request, cancellationToken);
        return TypedResults.Created($"/api/v1/websites/{website.Id}", website);
    }

    private static async Task<Ok<WebsiteResponse>> UpdateAsync(
        Guid id, UpdateWebsiteRequest request, WebsiteService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.UpdateAsync(id, request, cancellationToken));

    private static async Task<NoContent> DeleteAsync(Guid id, WebsiteService service, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
