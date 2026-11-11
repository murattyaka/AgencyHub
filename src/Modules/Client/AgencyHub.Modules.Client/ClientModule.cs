using AgencyHub.Infrastructure.Persistence;
using AgencyHub.Modules.Client.Application;
using AgencyHub.Modules.Client.Contracts;
using AgencyHub.Modules.Client.Infrastructure;
using AgencyHub.Modules.Identity.Contracts;
using AgencyHub.SharedKernel.Pagination;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AgencyHub.Modules.Client;

public static class ClientModule
{
    public static IServiceCollection AddClientModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<ClientModuleDbContext>(ClientModuleDbContext.Schema);
        services.AddScoped<ClientService>();
        services.AddScoped<IClientDirectory, ClientDirectory>();
        services.AddValidatorsFromAssemblyContaining<ClientService>(includeInternalTypes: true);
        return services;
    }

    public static IEndpointRouteBuilder MapClientEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/clients").WithTags("Clients");

        group.MapGet("/", ListAsync).RequireAuthorization(AuthorizationPolicies.AgencyMember).WithName("ListClients");
        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization(AuthorizationPolicies.AgencyMember).WithName("GetClient");
        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.AgencyEditor).WithName("CreateClient");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.AgencyEditor).WithName("UpdateClient");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.AgencyAdmin).WithName("DeleteClient");

        return endpoints;
    }

    private static async Task<Ok<PagedResult<ClientResponse>>> ListAsync(
        [AsParameters] ClientListQuery query, ClientService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ListAsync(query, cancellationToken));

    private static async Task<Ok<ClientResponse>> GetAsync(Guid id, ClientService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAsync(id, cancellationToken));

    private static async Task<Created<ClientResponse>> CreateAsync(
        CreateClientRequest request, ClientService service, CancellationToken cancellationToken)
    {
        var client = await service.CreateAsync(request, cancellationToken);
        return TypedResults.Created($"/api/v1/clients/{client.Id}", client);
    }

    private static async Task<Ok<ClientResponse>> UpdateAsync(
        Guid id, UpdateClientRequest request, ClientService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.UpdateAsync(id, request, cancellationToken));

    private static async Task<NoContent> DeleteAsync(Guid id, ClientService service, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
