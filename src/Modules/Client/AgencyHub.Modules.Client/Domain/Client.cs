using AgencyHub.SharedKernel.Domain;
using AgencyHub.SharedKernel.Tenancy;

namespace AgencyHub.Modules.Client.Domain;

internal sealed class Client : Entity, ITenantOwned, IAuditableEntity
{
    public const int NameMaxLength = 200;
    public const int ContactNameMaxLength = 100;
    public const int ContactEmailMaxLength = 256;
    public const int NotesMaxLength = 2000;

    private Client()
    {
    }

    public Client(string name, string? contactName, string? contactEmail, string? notes)
    {
        Update(name, contactName, contactEmail, notes, ClientStatus.Active);
    }

    public Guid AgencyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? ContactName { get; private set; }

    public string? ContactEmail { get; private set; }

    public string? Notes { get; private set; }

    public ClientStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public void Update(string name, string? contactName, string? contactEmail, string? notes, ClientStatus status)
    {
        Name = name.Trim();
        ContactName = Normalize(contactName);
        ContactEmail = Normalize(contactEmail);
        Notes = Normalize(notes);
        Status = status;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal enum ClientStatus
{
    Active = 1,
    Archived = 2,
}
