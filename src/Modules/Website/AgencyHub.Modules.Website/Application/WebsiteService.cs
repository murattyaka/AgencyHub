using System.Linq.Expressions;
using AgencyHub.Infrastructure.Persistence;
using AgencyHub.Modules.Project.Contracts;
using AgencyHub.Modules.Website.Domain;
using AgencyHub.Modules.Website.Infrastructure;
using AgencyHub.SharedKernel.Errors;
using AgencyHub.SharedKernel.Pagination;
using FluentValidation;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AgencyHub.Modules.Website.Application;

internal sealed record WebsiteListQuery(int? Page, int? PageSize, string? Search, Guid? ProjectId, WebsiteStatus? Status);

internal sealed record CreateWebsiteRequest(Guid ProjectId, string Url, string? Name);

internal sealed record UpdateWebsiteRequest(string Url, string? Name, WebsiteStatus Status);

internal sealed record WebsiteResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string Url,
    string Host,
    WebsiteStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

internal static class WebsiteValidationRules
{
    public static void ValidWebsiteUrl<T>(this IRuleBuilder<T, string> rule) =>
        rule.Custom((url, context) =>
        {
            if (!WebsiteUrlPolicy.TryNormalize(url, out _, out var error))
            {
                context.AddFailure(error);
            }
        });
}

internal sealed class CreateWebsiteRequestValidator : AbstractValidator<CreateWebsiteRequest>
{
    public CreateWebsiteRequestValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Url).ValidWebsiteUrl();
        RuleFor(x => x.Name).MaximumLength(Domain.Website.NameMaxLength);
    }
}

internal sealed class UpdateWebsiteRequestValidator : AbstractValidator<UpdateWebsiteRequest>
{
    public UpdateWebsiteRequestValidator()
    {
        RuleFor(x => x.Url).ValidWebsiteUrl();
        RuleFor(x => x.Name).MaximumLength(Domain.Website.NameMaxLength);
        RuleFor(x => x.Status).IsInEnum();
    }
}

internal sealed class WebsiteListQueryValidator : AbstractValidator<WebsiteListQuery>
{
    public WebsiteListQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
    }
}

internal sealed class WebsiteService
{
    private static readonly Expression<Func<Domain.Website, WebsiteResponse>> ToResponse = w =>
        new WebsiteResponse(w.Id, w.ProjectId, w.Name, w.Url, w.Host, w.Status, w.CreatedAtUtc, w.UpdatedAtUtc);

    private static readonly Func<Domain.Website, WebsiteResponse> MapResponse = ToResponse.Compile();

    private readonly WebsiteModuleDbContext _db;
    private readonly IProjectDirectory _projects;
    private readonly IValidator<CreateWebsiteRequest> _createValidator;
    private readonly IValidator<UpdateWebsiteRequest> _updateValidator;
    private readonly IValidator<WebsiteListQuery> _listValidator;

    public WebsiteService(
        WebsiteModuleDbContext db,
        IProjectDirectory projects,
        IValidator<CreateWebsiteRequest> createValidator,
        IValidator<UpdateWebsiteRequest> updateValidator,
        IValidator<WebsiteListQuery> listValidator)
    {
        _db = db;
        _projects = projects;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _listValidator = listValidator;
    }

    public async Task<PagedResult<WebsiteResponse>> ListAsync(WebsiteListQuery query, CancellationToken cancellationToken)
    {
        await _listValidator.ValidateAndThrowAsync(query, cancellationToken);

        var websites = _db.Websites.AsNoTracking();
        if (query.ProjectId is { } projectId)
        {
            websites = websites.Where(w => w.ProjectId == projectId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            websites = websites.Where(w => w.Name.Contains(search) || w.Host.Contains(search));
        }

        if (query.Status is { } status)
        {
            websites = websites.Where(w => w.Status == status);
        }

        return await websites
            .OrderBy(w => w.Name).ThenBy(w => w.Id)
            .Select(ToResponse)
            .ToPagedResultAsync(new PageRequest { Page = query.Page ?? 1, PageSize = query.PageSize ?? PageRequest.DefaultPageSize }, cancellationToken);
    }

    public async Task<WebsiteResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.Websites.AsNoTracking().Where(w => w.Id == id).Select(ToResponse).SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Website", id);

    public async Task<WebsiteResponse> CreateAsync(CreateWebsiteRequest request, CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var url = Normalize(request.Url);

        // Tenant-scoped: a project of another agency is reported exactly like a missing one.
        if (!await _projects.ExistsAsync(request.ProjectId, cancellationToken))
        {
            throw new BusinessRuleException("The specified project does not exist.");
        }

        await EnsureUrlIsFreeAsync(url, exceptId: null, cancellationToken);

        var website = new Domain.Website(request.ProjectId, url, request.Name);
        _db.Websites.Add(website);
        await SaveAsync(cancellationToken);
        return MapResponse(website);
    }

    public async Task<WebsiteResponse> UpdateAsync(Guid id, UpdateWebsiteRequest request, CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var url = Normalize(request.Url);
        var website = await FindAsync(id, cancellationToken);
        await EnsureUrlIsFreeAsync(url, exceptId: id, cancellationToken);

        website.Update(url, request.Name, request.Status);
        await SaveAsync(cancellationToken);
        return MapResponse(website);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var website = await FindAsync(id, cancellationToken);
        _db.Websites.Remove(website);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static NormalizedWebsiteUrl Normalize(string url) =>
        WebsiteUrlPolicy.TryNormalize(url, out var normalized, out _)
            ? normalized!
            : throw new InvalidOperationException("URL passed validation but could not be normalized.");

    private async Task EnsureUrlIsFreeAsync(NormalizedWebsiteUrl url, Guid? exceptId, CancellationToken cancellationToken)
    {
        if (await _db.Websites.AnyAsync(w => w.Url == url.Url && w.Id != exceptId, cancellationToken))
        {
            throw new ConflictException("This website is already registered in your agency.");
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException("This website is already registered in your agency.");
        }
    }

    private async Task<Domain.Website> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.Websites.SingleOrDefaultAsync(w => w.Id == id, cancellationToken)
        ?? throw new NotFoundException("Website", id);
}
