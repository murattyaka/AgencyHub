namespace AgencyHub.SharedKernel.Security;

/// <summary>
/// The user executing the current unit of work. <see cref="UserId"/> is null for system/background work.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
}
