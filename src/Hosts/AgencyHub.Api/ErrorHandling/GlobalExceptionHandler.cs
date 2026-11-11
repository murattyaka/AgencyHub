using AgencyHub.SharedKernel.Errors;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AgencyHub.Api.ErrorHandling;

/// <summary>
/// Single place where exceptions become HTTP responses (RFC 9457 ProblemDetails).
/// Unexpected errors never expose exception messages or stack traces to the client.
/// </summary>
internal sealed partial class GlobalExceptionHandler : IExceptionHandler
{
    private const int ClientClosedRequest = 499;

    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            LogRequestAborted(_logger, httpContext.Request.Path);
            httpContext.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        var problem = exception switch
        {
            ValidationException validation => CreateValidationProblem(validation),
            // Malformed JSON, wrong parameter types (thrown instead of returned in Development).
            BadHttpRequestException badRequest => Problem(badRequest.StatusCode, "Bad request", "The request is malformed."),
            AuthenticationFailedException => Problem(StatusCodes.Status401Unauthorized, "Unauthorized", "Authentication failed."),
            NotFoundException notFound => Problem(StatusCodes.Status404NotFound, "Resource not found", notFound.Message),
            ForbiddenAccessException forbidden => Problem(StatusCodes.Status403Forbidden, "Forbidden", forbidden.Message),
            TenantIsolationException => Problem(StatusCodes.Status403Forbidden, "Forbidden", detail: null),
            ConflictException conflict => Problem(StatusCodes.Status409Conflict, "Conflict", conflict.Message),
            BusinessRuleException rule => Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violation", rule.Message),
            _ => Problem(StatusCodes.Status500InternalServerError, "An unexpected error occurred.", detail: null),
        };

        switch (exception)
        {
            case TenantIsolationException:
                LogTenantIsolationViolation(_logger, exception, httpContext.Request.Path);
                break;
            case AppException or ValidationException or BadHttpRequestException:
                var exceptionType = exception.GetType().Name;
                LogHandledException(_logger, exceptionType, httpContext.Request.Path);
                break;
            default:
                LogUnhandledException(_logger, exception, httpContext.Request.Path);
                break;
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Problem(int status, string title, string? detail) =>
        new() { Status = status, Title = title, Detail = detail };

    private static ValidationProblemDetails CreateValidationProblem(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(e => e.PropertyName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

        return new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Detail = "One or more validation errors occurred.",
        };
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Request aborted by client: {Path}")]
    private static partial void LogRequestAborted(ILogger logger, PathString path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {ExceptionType} for {Path}")]
    private static partial void LogHandledException(ILogger logger, string exceptionType, PathString path);

    [LoggerMessage(EventId = 4030, Level = LogLevel.Warning, Message = "SECURITY: tenant isolation violation blocked for {Path}")]
    private static partial void LogTenantIsolationViolation(ILogger logger, Exception exception, PathString path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Path}")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception, PathString path);
}
