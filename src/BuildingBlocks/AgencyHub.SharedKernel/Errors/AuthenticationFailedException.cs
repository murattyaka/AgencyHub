namespace AgencyHub.SharedKernel.Errors;

/// <summary>
/// Maps to 401. The message is deliberately generic so callers cannot distinguish
/// "unknown user", "wrong password", "locked out" or "revoked token".
/// </summary>
public sealed class AuthenticationFailedException : AppException
{
    public AuthenticationFailedException(string message = "Authentication failed.")
        : base(message)
    {
    }
}
