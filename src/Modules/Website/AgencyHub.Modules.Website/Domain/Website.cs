using AgencyHub.SharedKernel.Domain;
using AgencyHub.SharedKernel.Tenancy;

namespace AgencyHub.Modules.Website.Domain;

internal sealed class Website : Entity, ITenantOwned, IAuditableEntity
{
    public const int NameMaxLength = 200;
    public const int HostMaxLength = 253;

    private Website()
    {
    }

    public Website(Guid projectId, NormalizedWebsiteUrl url, string? name)
    {
        ProjectId = projectId;
        Update(url, name, WebsiteStatus.Active);
    }

    public Guid AgencyId { get; private set; }

    /// <summary>Reference to the Project module (no database foreign key across module schemas).</summary>
    public Guid ProjectId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Normalized URL (lower-case scheme and host, no trailing slash, no query/fragment). Unique per agency.</summary>
    public string Url { get; private set; } = string.Empty;

    public string Host { get; private set; } = string.Empty;

    public WebsiteStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public void Update(NormalizedWebsiteUrl url, string? name, WebsiteStatus status)
    {
        Url = url.Url;
        Host = url.Host;
        Name = string.IsNullOrWhiteSpace(name) ? url.Host : name.Trim();
        Status = status;
    }
}

internal enum WebsiteStatus
{
    Active = 1,
    Paused = 2,
}
