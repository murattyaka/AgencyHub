namespace AgencyHub.SharedKernel.Tenancy;

/// <summary>
/// Marks an entity as belonging to exactly one agency (tenant).
/// Every type implementing this interface automatically receives:
///  - a global EF Core query filter restricting reads to the current agency, and
///  - write protection that assigns/validates <see cref="AgencyId"/> on SaveChanges.
/// </summary>
public interface ITenantOwned
{
    Guid AgencyId { get; }
}
