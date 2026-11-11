namespace AgencyHub.SharedKernel.Domain;

/// <summary>
/// Audit columns are populated by the persistence layer (AuditableEntityInterceptor);
/// application code must never set them directly.
/// </summary>
public interface IAuditableEntity
{
    DateTimeOffset CreatedAtUtc { get; }

    Guid? CreatedBy { get; }

    DateTimeOffset? UpdatedAtUtc { get; }

    Guid? UpdatedBy { get; }
}
