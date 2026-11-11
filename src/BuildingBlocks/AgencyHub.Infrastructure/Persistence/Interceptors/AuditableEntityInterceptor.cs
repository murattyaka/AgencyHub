using AgencyHub.SharedKernel.Domain;
using AgencyHub.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgencyHub.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Populates audit columns of <see cref="IAuditableEntity"/> and prevents them from being overwritten.
/// </summary>
public sealed class AuditableEntityInterceptor : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public AuditableEntityInterceptor(TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        ApplyAuditValues(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        ApplyAuditValues(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAuditValues(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var userId = _currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(nameof(IAuditableEntity.CreatedAtUtc)).CurrentValue = now;
                    entry.Property(nameof(IAuditableEntity.CreatedBy)).CurrentValue = userId;
                    break;

                case EntityState.Modified:
                    entry.Property(nameof(IAuditableEntity.CreatedAtUtc)).IsModified = false;
                    entry.Property(nameof(IAuditableEntity.CreatedBy)).IsModified = false;
                    entry.Property(nameof(IAuditableEntity.UpdatedAtUtc)).CurrentValue = now;
                    entry.Property(nameof(IAuditableEntity.UpdatedBy)).CurrentValue = userId;
                    break;
            }
        }
    }
}
