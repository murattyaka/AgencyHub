using AgencyHub.SharedKernel.Domain;

namespace AgencyHub.Modules.Agency.Domain;

/// <summary>
/// The tenant itself. Not <c>ITenantOwned</c>: access is restricted by comparing <see cref="Entity.Id"/>
/// with the tenant of the current request (there is no agency id in any route).
/// </summary>
internal sealed class Agency : Entity, IAuditableEntity
{
    public const int NameMaxLength = 100;

    private Agency()
    {
    }

    public Agency(string name)
    {
        Rename(name);
        Status = AgencyStatus.Active;
    }

    public string Name { get; private set; } = string.Empty;

    public AgencyStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public void Rename(string name) => Name = name.Trim();
}

internal enum AgencyStatus
{
    Active = 1,
    Suspended = 2,
}
