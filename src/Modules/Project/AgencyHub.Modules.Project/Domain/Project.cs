using AgencyHub.SharedKernel.Domain;
using AgencyHub.SharedKernel.Tenancy;

namespace AgencyHub.Modules.Project.Domain;

/// <summary>A piece of work for one client. <see cref="ClientId"/> is fixed at creation.</summary>
internal sealed class Project : Entity, ITenantOwned, IAuditableEntity
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;

    private Project()
    {
    }

    public Project(Guid clientId, string name, string? description)
    {
        ClientId = clientId;
        Update(name, description, ProjectStatus.Active);
    }

    public Guid AgencyId { get; private set; }

    /// <summary>Reference to the Client module (no database foreign key across module schemas).</summary>
    public Guid ClientId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public ProjectStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public void Update(string name, string? description, ProjectStatus status)
    {
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Status = status;
    }
}

internal enum ProjectStatus
{
    Active = 1,
    Completed = 2,
    Archived = 3,
}
