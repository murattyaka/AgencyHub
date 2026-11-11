using AgencyHub.Infrastructure.Persistence;
using AgencyHub.Modules.Client.Domain;
using AgencyHub.Modules.Client.Infrastructure;
using AgencyHub.Modules.Project.Contracts;
using AgencyHub.SharedKernel.Errors;
using AgencyHub.SharedKernel.Pagination;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AgencyHub.Modules.Client.Application;

internal sealed record ClientListQuery(int? Page, int? PageSize, string? Search, ClientStatus? Status);

internal sealed record CreateClientRequest(string Name, string? ContactName, string? ContactEmail, string? Notes);

internal sealed record UpdateClientRequest(string Name, string? ContactName, string? ContactEmail, string? Notes, ClientStatus Status);

internal sealed record ClientResponse(
    Guid Id,
    string Name,
    string? ContactName,
    string? ContactEmail,
    string? Notes,
    ClientStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

internal sealed class CreateClientRequestValidator : AbstractValidator<CreateClientRequest>
{
    public CreateClientRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Domain.Client.NameMaxLength);
        RuleFor(x => x.ContactName).MaximumLength(Domain.Client.ContactNameMaxLength);
        RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(Domain.Client.ContactEmailMaxLength)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        RuleFor(x => x.Notes).MaximumLength(Domain.Client.NotesMaxLength);
    }
}

internal sealed class UpdateClientRequestValidator : AbstractValidator<UpdateClientRequest>
{
    public UpdateClientRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Domain.Client.NameMaxLength);
        RuleFor(x => x.ContactName).MaximumLength(Domain.Client.ContactNameMaxLength);
        RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(Domain.Client.ContactEmailMaxLength)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        RuleFor(x => x.Notes).MaximumLength(Domain.Client.NotesMaxLength);
        RuleFor(x => x.Status).IsInEnum();
    }
}

internal sealed class ClientListQueryValidator : AbstractValidator<ClientListQuery>
{
    public ClientListQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
    }
}

/// <remarks>All queries go through the tenant query filter: other agencies' clients behave as non-existent (404).</remarks>
internal sealed class ClientService
{
    private readonly ClientModuleDbContext _db;
    private readonly IProjectDirectory _projects;
    private readonly IValidator<CreateClientRequest> _createValidator;
    private readonly IValidator<UpdateClientRequest> _updateValidator;
    private readonly IValidator<ClientListQuery> _listValidator;

    public ClientService(
        ClientModuleDbContext db,
        IProjectDirectory projects,
        IValidator<CreateClientRequest> createValidator,
        IValidator<UpdateClientRequest> updateValidator,
        IValidator<ClientListQuery> listValidator)
    {
        _db = db;
        _projects = projects;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _listValidator = listValidator;
    }

    public async Task<PagedResult<ClientResponse>> ListAsync(ClientListQuery query, CancellationToken cancellationToken)
    {
        await _listValidator.ValidateAndThrowAsync(query, cancellationToken);

        var clients = _db.Clients.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            clients = clients.Where(c => c.Name.Contains(search));
        }

        if (query.Status is { } status)
        {
            clients = clients.Where(c => c.Status == status);
        }

        return await clients
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(ToResponse)
            .ToPagedResultAsync(new PageRequest { Page = query.Page ?? 1, PageSize = query.PageSize ?? PageRequest.DefaultPageSize }, cancellationToken);
    }

    public async Task<ClientResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.Clients.AsNoTracking().Where(c => c.Id == id).Select(ToResponse).SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Client", id);

    public async Task<ClientResponse> CreateAsync(CreateClientRequest request, CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var client = new Domain.Client(request.Name, request.ContactName, request.ContactEmail, request.Notes);
        _db.Clients.Add(client);
        await _db.SaveChangesAsync(cancellationToken);
        return MapResponse(client);
    }

    public async Task<ClientResponse> UpdateAsync(Guid id, UpdateClientRequest request, CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var client = await FindAsync(id, cancellationToken);
        client.Update(request.Name, request.ContactName, request.ContactEmail, request.Notes, request.Status);
        await _db.SaveChangesAsync(cancellationToken);
        return MapResponse(client);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var client = await FindAsync(id, cancellationToken);
        if (await _projects.AnyForClientAsync(id, cancellationToken))
        {
            throw new ConflictException("The client has projects. Delete or move them first, or archive the client.");
        }

        _db.Clients.Remove(client);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Domain.Client> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.Clients.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
        ?? throw new NotFoundException("Client", id);

    private static readonly System.Linq.Expressions.Expression<Func<Domain.Client, ClientResponse>> ToResponse = c =>
        new ClientResponse(c.Id, c.Name, c.ContactName, c.ContactEmail, c.Notes, c.Status, c.CreatedAtUtc, c.UpdatedAtUtc);

    private static readonly Func<Domain.Client, ClientResponse> MapResponse = ToResponse.Compile();
}
