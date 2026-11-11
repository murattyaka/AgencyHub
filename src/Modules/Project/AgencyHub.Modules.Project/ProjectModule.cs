using AgencyHub.Infrastructure.Persistence;
using AgencyHub.Modules.Identity.Contracts;
using AgencyHub.Modules.Project.Application;
using AgencyHub.Modules.Project.Contracts;
using AgencyHub.Modules.Project.Infrastructure;
using AgencyHub.SharedKernel.Pagination;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AgencyHub.Modules.Project;

public static class ProjectModule
{
    public static IServiceCollection AddProjectModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<ProjectModuleDbContext>(ProjectModuleDbContext.Schema);
        services.AddScoped<ProjectService>();
        services.AddScoped<IProjectDirectory, ProjectDirectory>();
        services.AddValidatorsFromAssemblyContaining<ProjectService>(includeInternalTypes: true);
        return services;
    }

    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/projects").WithTags("Projects");

        group.MapGet("/", ListAsync).RequireAuthorization(AuthorizationPolicies.AgencyMember).WithName("ListProjects");
        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization(AuthorizationPolicies.AgencyMember).WithName("GetProject");
        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.AgencyEditor).WithName("CreateProject");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.AgencyEditor).WithName("UpdateProject");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.AgencyAdmin).WithName("DeleteProject");

        return endpoints;
    }

    private static async Task<Ok<PagedResult<ProjectResponse>>> ListAsync(
        [AsParameters] ProjectListQuery query, ProjectService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ListAsync(query, cancellationToken));

    private static async Task<Ok<ProjectResponse>> GetAsync(Guid id, ProjectService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAsync(id, cancellationToken));

    private static async Task<Created<ProjectResponse>> CreateAsync(
        CreateProjectRequest request, ProjectService service, CancellationToken cancellationToken)
    {
        var project = await service.CreateAsync(request, cancellationToken);
        return TypedResults.Created($"/api/v1/projects/{project.Id}", project);
    }

    private static async Task<Ok<ProjectResponse>> UpdateAsync(
        Guid id, UpdateProjectRequest request, ProjectService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.UpdateAsync(id, request, cancellationToken));

    private static async Task<NoContent> DeleteAsync(Guid id, ProjectService service, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
