namespace AgencyHub.SharedKernel.Domain;

/// <summary>
/// Base type for all persisted entities.
/// </summary>
/// <remarks>
/// The identifier is intentionally NOT generated here. EF Core's SQL Server provider assigns
/// a SQL Server-sequential GUID on Add(), which keeps clustered indexes from fragmenting.
/// (Guid.CreateVersion7 is time-ordered for .NET but NOT for SQL Server's uniqueidentifier sort order.)
/// See DECISION_LOG.md, ADR-006.
/// </remarks>
public abstract class Entity
{
    public Guid Id { get; protected set; }
}
