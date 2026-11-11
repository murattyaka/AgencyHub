using System.Globalization;
using System.Text.Json.Serialization;
using AgencyHub.Api.ErrorHandling;
using AgencyHub.Api.Security;
using AgencyHub.Infrastructure;
using AgencyHub.Modules.Agency;
using AgencyHub.Modules.Client;
using AgencyHub.Modules.Identity;
using AgencyHub.Modules.Project;
using AgencyHub.Modules.Website;
using AgencyHub.Modules.Identity.Contracts;
using AgencyHub.SharedKernel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

// Minimal console logger so failures during startup are still visible.
Log.Logger = new LoggerConfiguration().WriteTo.Console(formatProvider: CultureInfo.InvariantCulture).CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

    // ---- Logging & tracing ------------------------------------------------------------
    // preserveStaticLogger: each host owns its logger instead of overwriting the process-wide Log.Logger.
    builder.Services.AddSerilog(
        (services, logger) => logger
            .ReadFrom.Configuration(services.GetRequiredService<IConfiguration>())
            .ReadFrom.Services(services)
            .Enrich.FromLogContext(),
        preserveStaticLogger: true);

    var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("AgencyHub.Api"))
        .WithTracing(tracing =>
        {
            tracing.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"));
            tracing.AddHttpClientInstrumentation();
            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            }
        });

    // ---- Error handling ---------------------------------------------------------------
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    // ---- Cross-cutting infrastructure -------------------------------------------------
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
    builder.Services.AddPersistenceCore();
    builder.Services.AddApiRateLimiting();
    builder.Services.AddHealthChecks();
    builder.Services.AddOpenApi();

    // Enums travel as strings ("Active"), never as numbers.
    builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    // ---- Modules ------------------------------------------------------------------------
    builder.Services.AddIdentityModule();
    builder.Services.AddAgencyModule();
    builder.Services.AddClientModule();
    builder.Services.AddProjectModule();
    builder.Services.AddWebsiteModule();

    // Secure by default: every endpoint requires an agency-bound user unless it explicitly opts out
    // with AllowAnonymous (the allow-list is verified by EndpointSecurityTests).
    builder.Services.AddAuthorizationBuilder()
        .SetFallbackPolicy(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(IdentityClaimTypes.AgencyId)
            .Build());

    // Credentials are allowed for the refresh-token cookie; origins must therefore be explicit (never "*").
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

    builder.Services.AddHsts(options =>
    {
        options.MaxAge = TimeSpan.FromDays(365);
        options.IncludeSubDomains = true;
    });

    var app = builder.Build();

    // ---- Pipeline (order matters) -------------------------------------------------------
    // Request logging is outermost so it records the final status code (after exception handling),
    // and handled exceptions such as 401/404 are not logged as server errors.
    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseMiddleware<SecurityHeadersMiddleware>();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseCors();
    app.UseRateLimiter();

    // TenantResolutionMiddleware must run directly after authentication and before authorization.
    app.UseAuthentication();
    app.UseMiddleware<TenantResolutionMiddleware>();
    app.UseAuthorization();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().AllowAnonymous();
    }

    // Liveness: process is up (no dependency checks). Readiness: dependencies tagged "ready" are healthy.
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

    app.MapIdentityEndpoints();
    app.MapAgencyEndpoints(registrationRateLimitPolicy: RateLimitPolicies.Authentication);
    app.MapClientEndpoints();
    app.MapProjectEndpoints();
    app.MapWebsiteEndpoints();

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "AgencyHub.Api terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Entry point; partial declaration exposes it to WebApplicationFactory in integration tests.</summary>
public partial class Program;
