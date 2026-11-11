namespace AgencyHub.Modules.Client.Contracts;

/// <summary>Read-only queries other modules may run against clients. Always scoped to the current tenant.</summary>
public interface IClientDirectory
{
    /// <summary>False for unknown ids and for clients of other agencies (indistinguishable by design).</summary>
    Task<bool> ExistsAsync(Guid clientId, CancellationToken cancellationToken);
}
