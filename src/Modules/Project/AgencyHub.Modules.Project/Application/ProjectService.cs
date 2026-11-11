using System.Linq.Expressions;
using AgencyHub.Infrastructure.Persistence;
using AgencyHub.Modules.Client.Contracts;
using AgencyHub.Modules.Project.Domain;
using AgencyHub.Modules.Project.Infrastructure;
using AgencyHub.Modules.Website.Contracts;
using AgencyHub.SharedKernel.Errors;
using AgencyHub.SharedKernel.Pagination;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AgencyHub.Modules.Project.Application;

internal sealed record ProjectListQuery(int? Page, int? PageSize, string? Search, Guid? ClientId, ProjectStatus? Status);

internal sealed record CreateProjectRequest(Guid ClientId, string Name, string? Description);

internal sealed record UpdateProjectRequest(string Name, string? Description, ProjectStatus Status);

internal sealed record ProjectResponse(
    Guid Id,
    Guid ClientId,
    string Name,
    string? Description,
    ProjectStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

internal sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Domain.Project.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(Domain.Project.DescriptionMaxLength);
    }
}

internal sealed class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Domain.Project.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(Domain.Project.DescriptionMaxLength);
        RuleFor(x => x.Status).IsInEnum();
    }
}

internal sealed class ProjectListQueryValidator : AbstractValidator<ProjectListQuery>
{
    public ProjectListQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
    }
}

internal sealed class ProjectService
{
    private static readonly Expression<Func<Domain.Project, ProjectResponse>> ToResponse = p =>
        new ProjectResponse(p.Id, p.ClientId, p.Name, p.Description, p.Status, p.CreatedAtUtc, p.UpdatedAtUtc);

    private static readonly Func<Domain.Project, ProjectResponse> MapResponse = ToResponse.Compile();

    private readonly ProjectModuleDbContext _db;
    private readonly IClientDirectory _clients;
    private readonly IWebsiteDirectory _websites;
    private readonly IValidator<CreateProjectRequest> _createValidator;
    private readonly IValidator<UpdateProjectRequest> _updateValidator;
    private readonly IValidator<ProjectListQuery> _listValidator;

    public ProjectService(
        ProjectModuleDbContext db,
        IClientDirectory clients,
        IWebsiteDirectory websites,
        IValidator<CreateProjectRequest> createValidator,
        IValidator<UpdateProjectRequest> updateValidator,
        IValidator<ProjectListQuery> listValidator)
    {
        _db = db;
        _clients = clients;
        _websites = websites;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _listValidator = listValidator;
    }

    public async Task<PagedResult<ProjectResponse>> ListAsync(ProjectListQuery query, CancellationToken cancellationToken)
    {
        await _listValidator.ValidateAndThrowAsync(query, cancellationToken);

        var projects = _db.Projects.AsNoTracking();
        if (query.ClientId is { } clientId)
        {
            projects = projects.Where(p => p.ClientId == clientId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            projects = projects.Where(p => p.Name.Contains(search));
        }

        if (query.Status is { } status)
        {
            projects = projects.Where(p => p.Status == status);
        }

        return await projects
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Select(ToResponse)
            .ToPagedResultAsync(new PageRequest { Page = query.Page ?? 1, PageSize = query.PageSize ?? PageRequest.DefaultPageSize }, cancellationToken);
    }

    public async Task<ProjectResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.Projects.AsNoTracking().Where(p => p.Id == id).Select(ToResponse).SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Project", id);

    public async Task<ProjectResponse> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);

        // Tenant-scoped: a client of another agency is reported exactly like a missing one.
        if (!await _clients.ExistsAsync(request.ClientId, cancellationToken))
        {
            throw new BusinessRuleException("The specified client does not exist.");
        }

        var project = new Domain.Project(request.ClientId, request.Name, request.Description);
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(cancellationToken);
        return MapResponse(project);
    }

    public async Task<ProjectResponse> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var project = await FindAsync(id, cancellationToken);
        project.Update(request.Name, request.Description, request.Status);
        await _db.SaveChangesAsync(cancellationToken);
        return MapResponse(project);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await FindAsync(id, cancellationToken);
        if (await _websites.AnyForProjectAsync(id, cancellationToken))
        {
            throw new ConflictException("The project has websites. Delete them first, or archive the project.");
        }

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Domain.Project> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.Projects.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
        ?? throw new NotFoundException("Project", id);
}
