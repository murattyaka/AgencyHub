using AgencyHub.Modules.Identity.Contracts;
using AgencyHub.Modules.Identity.Domain;
using AgencyHub.Modules.Identity.Infrastructure;
using AgencyHub.SharedKernel.Errors;
using AgencyHub.SharedKernel.Tenancy;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AgencyHub.Modules.Identity.Application;

internal sealed class IdentityProvisioning : IIdentityProvisioning
{
    private readonly IdentityModuleDbContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IValidator<CreateAgencyUserRequest> _validator;
    private readonly ITenantContext _tenantContext;

    public IdentityProvisioning(
        IdentityModuleDbContext db,
        IPasswordHasher<User> passwordHasher,
        IValidator<CreateAgencyUserRequest> validator,
        ITenantContext tenantContext)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _validator = validator;
        _tenantContext = tenantContext;
    }

    public async Task<Guid> CreateUserWithMembershipAsync(CreateAgencyUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_tenantContext.HasTenant)
        {
            throw new TenantIsolationException("A tenant context is required to create agency users.");
        }

        await _validator.ValidateAndThrowAsync(request, cancellationToken);

        var normalizedEmail = User.NormalizeEmail(request.Email);
        if (await _db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            throw new ConflictException("A user with this email already exists.");
        }

        var user = new User(request.Email, request.DisplayName);
        user.SetPasswordHash(_passwordHasher.HashPassword(user, request.Password));
        _db.Users.Add(user);
        _db.Memberships.Add(new AgencyMembership(user.Id, request.Role));

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Unique index race: the same email was registered concurrently.
            throw new ConflictException("A user with this email already exists.");
        }

        return user.Id;
    }
}
