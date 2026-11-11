namespace AgencyHub.Modules.Website.Contracts;

/// <summary>Read-only queries other modules may run against websites. Always scoped to the current tenant.</summary>
public interface IWebsiteDirectory
{
    Task<bool> AnyForProjectAsync(Guid projectId, CancellationToken cancellationToken);
}
