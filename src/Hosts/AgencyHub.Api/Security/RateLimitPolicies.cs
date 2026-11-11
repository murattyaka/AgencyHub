using System.Globalization;
using System.Threading.RateLimiting;
using AgencyHub.Modules.Identity;
using Microsoft.AspNetCore.RateLimiting;

namespace AgencyHub.Api.Security;

internal static class RateLimitPolicies
{
    /// <summary>Strict policy for login, refresh and logout (brute-force protection).</summary>
    public const string Authentication = IdentityModule.AuthenticationRateLimitPolicy;

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(_ => { });

        // Configured lazily so limits come from the final configuration (appsettings, env vars, tests).
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var globalPermitLimit = configuration.GetValue("RateLimiting:GlobalPermitLimitPerMinute", 300);
            var authenticationPermitLimit = configuration.GetValue("RateLimiting:AuthenticationPermitLimitPerMinute", 5);

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            // Global limit per client IP. Per-agency quotas will be added with the scan modules.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = globalPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            options.AddPolicy(Authentication, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = authenticationPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        return services;
    }

    // Behind IIS (in-process) RemoteIpAddress is the real client address. If a reverse proxy/load balancer
    // is added later, configure ForwardedHeaders with KnownProxies first; otherwise this key is spoofable.
    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
