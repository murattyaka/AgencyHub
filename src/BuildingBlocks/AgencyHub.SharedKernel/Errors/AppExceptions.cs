namespace AgencyHub.SharedKernel.Errors;

// Application exceptions are mapped to HTTP responses centrally (GlobalExceptionHandler).
// Their messages may be shown to API clients, so never put secrets or internal details in them.

public abstract class AppException : Exception
{
    protected AppException(string message)
        : base(message)
    {
    }
}

/// <summary>Maps to 404. Also used for resources of other tenants, so their existence is not revealed.</summary>
public sealed class NotFoundException : AppException
{
    public NotFoundException(string resourceName, object key)
        : base($"{resourceName} '{key}' was not found.")
    {
    }
}

/// <summary>Maps to 403.</summary>
public sealed class ForbiddenAccessException : AppException
{
    public ForbiddenAccessException(string message = "You do not have permission to perform this action.")
        : base(message)
    {
    }
}

/// <summary>Maps to 409.</summary>
public sealed class ConflictException : AppException
{
    public ConflictException(string message)
        : base(message)
    {
    }
}

/// <summary>Maps to 422.</summary>
public sealed class BusinessRuleException : AppException
{
    public BusinessRuleException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Raised when code attempts to read or write data outside the current tenant.
/// Maps to 403 with a generic message; the details are logged as a security event.
/// </summary>
public sealed class TenantIsolationException : AppException
{
    public TenantIsolationException(string message)
        : base(message)
    {
    }
}
