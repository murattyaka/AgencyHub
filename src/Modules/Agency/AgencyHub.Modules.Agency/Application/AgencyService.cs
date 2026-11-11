using AgencyHub.Modules.Agency.Infrastructure;
using AgencyHub.Modules.Identity.Contracts;
using AgencyHub.SharedKernel.Errors;
using AgencyHub.SharedKernel.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgencyHub.Modules.Agency.Application;

internal sealed record RegisterAgencyRequest(string AgencyName, string OwnerEmail, string OwnerDisplayName, string OwnerPassword);

internal sealed record RegisterAgencyResult(Guid AgencyId, Guid OwnerUserId);

internal sealed record UpdateAgencyRequest(string Name);

internal sealed record AgencyResponse(Guid Id, string Name, string Status, DateTimeOffset CreatedAtUtc);

internal sealed class AgencyRegistrationOptions
{
    public const string SectionName = "AgencyRegistration";

    /// <summary>Public self-service sign-up. Disabled by default; see DECISION_LOG P-7.</summary>
    public bool Enabled { get; set; }
}

internal sealed class RegisterAgencyRequestValidator : AbstractValidator<RegisterAgencyRequest>
{
    public RegisterAgencyRequestValidator()
    {
        RuleFor(x => x.AgencyName).NotEmpty().MinimumLength(2).MaximumLength(Domain.Agency.NameMaxLength);
        RuleFor(x => x.OwnerEmail).NotEmpty();
        RuleFor(x => x.OwnerDisplayName).NotEmpty();
        RuleFor(x => x.OwnerPassword).NotEmpty();
        // Detailed user rules (email format, password policy) are enforced by IIdentityProvisioning.
    }
}

internal sealed class UpdateAgencyRequestValidator : AbstractValidator<UpdateAgencyRequest>
{
    public UpdateAgencyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MinimumLength(2).MaximumLength(Domain.Agency.NameMaxLength);
    }
}

internal sealed partial class AgencyService
{
    private readonly AgencyModuleDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantContextSetter _tenantContextSetter;
    private readonly IIdentityProvisioning _identityProvisioning;
    private readonly IValidator<RegisterAgencyRequest> _registerValidator;
    private readonly IValidator<UpdateAgencyRequest> _updateValidator;
    private readonly IOptions<AgencyRegistrationOptions> _registrationOptions;
    private readonly ILogger<AgencyService> _logger;

    public AgencyService(
        AgencyModuleDbContext db,
        ITenantContext tenantContext,
        ITenantContextSetter tenantContextSetter,
        IIdentityProvisioning identityProvisioning,
        IValidator<RegisterAgencyRequest> registerValidator,
        IValidator<UpdateAgencyRequest> updateValidator,
        IOptions<AgencyRegistrationOptions> registrationOptions,
        ILogger<AgencyService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _tenantContextSetter = tenantContextSetter;
        _identityProvisioning = identityProvisioning;
        _registerValidator = registerValidator;
        _updateValidator = updateValidator;
        _registrationOptions = registrationOptions;
        _logger = logger;
    }

    /// <summary>
    /// Creates the agency and its first Owner. The two writes go to different module contexts, so instead of a
    /// distributed transaction the agency row is removed again if creating the owner fails (ADR-013).
    /// </summary>
    public async Task<RegisterAgencyResult> RegisterAsync(RegisterAgencyRequest request, CancellationToken cancellationToken)
    {
        if (!_registrationOptions.Value.Enabled)
        {
            throw new NotFoundException("Resource", "agency-registration");
        }

        if (_tenantContext.HasTenant)
        {
            throw new ForbiddenAccessException("An authenticated agency user cannot register a new agency.");
        }

        await _registerValidator.ValidateAndThrowAsync(request, cancellationToken);

        var agency = new Domain.Agency(request.AgencyName);
        _db.Agencies.Add(agency);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            _tenantContextSetter.SetTenant(agency.Id);
            var ownerId = await _identityProvisioning.CreateUserWithMembershipAsync(
                new CreateAgencyUserRequest(request.OwnerEmail, request.OwnerDisplayName, request.OwnerPassword, AgencyRole.Owner),
                cancellationToken);

            LogAgencyRegistered(_logger, agency.Id, ownerId);
            return new RegisterAgencyResult(agency.Id, ownerId);
        }
        catch
        {
            // Compensation: never leave an agency without an owner. CancellationToken.None: must run even if the client aborted.
            _db.Agencies.Remove(agency);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<AgencyResponse> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var agency = await FindCurrentAsync(tracking: false, cancellationToken);
        return ToResponse(agency);
    }

    public async Task<AgencyResponse> UpdateCurrentAsync(UpdateAgencyRequest request, CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        var agency = await FindCurrentAsync(tracking: true, cancellationToken);
        agency.Rename(request.Name);
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(agency);
    }

    private async Task<Domain.Agency> FindCurrentAsync(bool tracking, CancellationToken cancellationToken)
    {
        var agencyId = _tenantContext.AgencyId ?? throw new ForbiddenAccessException();
        var query = tracking ? _db.Agencies : _db.Agencies.AsNoTracking();
        return await query.SingleOrDefaultAsync(a => a.Id == agencyId, cancellationToken)
            ?? throw new NotFoundException("Agency", agencyId);
    }

    private static AgencyResponse ToResponse(Domain.Agency agency) =>
        new(agency.Id, agency.Name, agency.Status.ToString(), agency.CreatedAtUtc);

    [LoggerMessage(Level = LogLevel.Information, Message = "Agency {AgencyId} registered with owner {OwnerUserId}")]
    private static partial void LogAgencyRegistered(ILogger logger, Guid agencyId, Guid ownerUserId);
}
