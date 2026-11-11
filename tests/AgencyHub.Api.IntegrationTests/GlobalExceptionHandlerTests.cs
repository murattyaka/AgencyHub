using System.Net;
using System.Text.Json;
using AgencyHub.Api.ErrorHandling;
using AgencyHub.SharedKernel.Errors;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgencyHub.Api.IntegrationTests;

public sealed class GlobalExceptionHandlerTests
{
    private const string Secret = "Server=db;Password=SuperSecret123";

    [Fact]
    public async Task Unexpected_exception_returns_500_without_leaking_message_or_stack_trace()
    {
        var (status, body) = await HandleAsync(new InvalidOperationException(Secret));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.DoesNotContain("SuperSecret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", body, StringComparison.Ordinal);
        Assert.Contains("traceId", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tenant_isolation_violation_returns_generic_403()
    {
        var (status, body) = await HandleAsync(new TenantIsolationException("Cross-tenant write attempt on 'Website' was blocked."));

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.DoesNotContain("Website", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(AuthenticationFailedException), HttpStatusCode.Unauthorized)]
    [InlineData(typeof(NotFoundException), HttpStatusCode.NotFound)]
    [InlineData(typeof(ForbiddenAccessException), HttpStatusCode.Forbidden)]
    [InlineData(typeof(ConflictException), HttpStatusCode.Conflict)]
    [InlineData(typeof(BusinessRuleException), HttpStatusCode.UnprocessableEntity)]
    public async Task Application_exceptions_map_to_status_codes(Type exceptionType, HttpStatusCode expected)
    {
        Exception exception = exceptionType == typeof(NotFoundException)
            ? new NotFoundException("Website", Guid.Empty)
            : (Exception)Activator.CreateInstance(exceptionType, "message")!;

        var (status, _) = await HandleAsync(exception);

        Assert.Equal((int)expected, status);
    }

    [Fact]
    public async Task Validation_exception_returns_400_with_field_errors()
    {
        var exception = new ValidationException([new ValidationFailure("Url", "Url must be absolute.")]);

        var (status, body) = await HandleAsync(exception);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        using var json = JsonDocument.Parse(body);
        var urlErrors = json.RootElement.GetProperty("errors").GetProperty("Url");
        Assert.Equal("Url must be absolute.", urlErrors[0].GetString());
    }

    [Fact]
    public async Task Client_abort_is_not_reported_as_server_error()
    {
        var (status, _) = await HandleAsync(new OperationCanceledException(), clientAborted: true);

        Assert.Equal(499, status);
    }

    private static async Task<(int Status, string Body)> HandleAsync(Exception exception, bool clientAborted = false)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            RequestAborted = clientAborted ? new CancellationToken(canceled: true) : CancellationToken.None,
        };
        context.Request.Path = "/test";
        context.Response.Body = new MemoryStream();

        var handler = new GlobalExceptionHandler(
            services.GetRequiredService<Microsoft.AspNetCore.Http.IProblemDetailsService>(),
            NullLogger<GlobalExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(context, exception, TestContext.Current.CancellationToken);
        Assert.True(handled);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return (context.Response.StatusCode, await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }
}
