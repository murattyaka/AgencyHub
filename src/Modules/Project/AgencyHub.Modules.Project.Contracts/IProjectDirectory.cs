namespace AgencyHub.Modules.Project.Contracts;

/// <summary>Read-only queries other modules may run against projects. Always scoped to the current tenant.</summary>
public interface IProjectDirectory
{
    /// <summary>False for unknown ids and for projects of other agencies.</summary>
    Task<bool> ExistsAsync(Guid projectId, CancellationToken cancellationToken);

    Task<bool> AnyForClientAsync(Guid clientId, CancellationToken cancellationToken);
}
